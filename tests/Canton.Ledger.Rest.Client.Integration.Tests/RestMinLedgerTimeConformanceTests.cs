// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Peaceful.Canton.Localnet.Testing;
using Richtypes;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Integration.Tests;

/// <summary>
/// LocalNet conformance coverage that <c>minLedgerTimeRel</c> reaches the participant as a bound it
/// actually enforces. The participant discards a duration whose value is not the
/// <c>{"seconds":…,"nanos":…}</c> object it serves, answering 200 and committing immediately, so a
/// dropped bound and an honored one differ only in observable behaviour — never in status code.
/// Every assertion here therefore has to be discriminating.
/// <para>
/// Three submissions differing in exactly one field make the drop observable. An unbound create
/// fixes the baseline: a healthy lane commits it promptly. The same create carrying the
/// proto3-canonical duration string our own specification declares must commit just as promptly,
/// because the participant discards that shape — that leg pins the defect this converter exists to
/// fix, and it is the leg that starts failing once the participant honors the declared shape
/// natively. The same create carrying the adapted object must not commit inside the observation
/// window, because an hour-long bound is genuinely held.
/// </para>
/// <para>
/// The held leg is asserted as "did not commit inside the window" rather than on the 503 a held
/// submission eventually draws, because that status arrives on the participant's own deadline
/// rather than ours. Not committing is on its own too weak to prove anything — a submission
/// rejected outright would satisfy it — so that leg must also outlast the window a prompt commit
/// answers in. The unbound leg runs first and is required to be prompt, so a lane too slow to
/// commit anything cannot masquerade as a bound being honored.
/// </para>
/// <para>
/// The submissions are built through the same builder and serializer options the client submits
/// with, so what is measured here is what a caller sends, not a hand-written approximation of it.
/// The bound is placed on the raw wire shape because no transport-neutral submission type carries a
/// minimum ledger time today.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
public class RestMinLedgerTimeConformanceTests
{
    private const string SubmitAndWaitPath = "/v2/commands/submit-and-wait";
    private const string MinLedgerTimeRelProperty = "minLedgerTimeRel";
    private const string SecondsProperty = "seconds";
    private const string NanosProperty = "nanos";
    private const string RelativeBound = "3600s";

    private static readonly TimeSpan ObservationWindow = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PromptCommit = TimeSpan.FromSeconds(10);

    private static string DarPath() => Path.Combine(
        AppContext.BaseDirectory, "testdata", "richtypes", "richtypes.dar");

    private static async Task<Party> NewOwnerAsync(RestConformanceLane lane, CancellationToken cancellationToken)
    {
        var darOutcome = await lane.Fixture.UploadDarAsync(DarPath(), cancellationToken);
        Assert.True(
            darOutcome is DarUploadOutcome.Uploaded or DarUploadOutcome.AlreadyKnown,
            $"Unexpected DAR upload outcome: {darOutcome}");

        var party = await lane.Fixture.AllocatePartyAsync(
            "rest-min-ledger-time", cancellationToken: cancellationToken);
        await lane.Fixture.GrantUserRightsAsync(
            lane.Fixture.ValidatorUserId, actAs: [party.PartyId], cancellationToken: cancellationToken);
        return new Party(party.PartyId);
    }

    [Fact]
    public async Task A_relative_minimum_ledger_time_binds_only_when_it_is_sent_as_the_served_Duration_object()
    {
        await using var lane = await RestConformanceLane.OpenAsync(TestContext.Current.CancellationToken);
        var owner = await NewOwnerAsync(lane, TestContext.Current.CancellationToken);
        using var wireClient = lane.CreateWireLevelClient();

        var adaptedBody = BoundCreateBody(owner);
        BoundOf(adaptedBody).Should().BeOfType<JsonObject>(
            "the converter must emit the object shape the participant reads")
            .Which.Should().ContainKeys(SecondsProperty, NanosProperty);

        var unbound = await SubmitAsync(wireClient, UnboundCreateBody(owner), TestContext.Current.CancellationToken);
        var specDeclared = await SubmitAsync(
            wireClient, SpecDeclaredBoundCreateBody(owner), TestContext.Current.CancellationToken);
        var adapted = await SubmitAsync(wireClient, adaptedBody, TestContext.Current.CancellationToken);

        unbound.Committed.Should().BeTrue(
            "an unbound create must commit for the two bound legs to mean anything, but the lane answered "
            + $"{unbound.Describe()}");
        unbound.Elapsed.Should().BeLessThan(PromptCommit);

        specDeclared.Committed.Should().BeTrue(
            "the participant discards a duration that is not an object, so the proto3 string our "
            + $"specification declares must reach the ledger unbound; it answered {specDeclared.Describe()}");
        specDeclared.Elapsed.Should().BeLessThan(PromptCommit);

        adapted.Committed.Should().BeFalse(
            "an hour-long minimum ledger time sent as the served object must be held rather than "
            + $"committed, but it answered {adapted.Describe()}");
        adapted.Elapsed.Should().BeGreaterThan(
            PromptCommit,
            "a held submission must outlast the window a prompt commit answers in; a fast rejection "
            + "would satisfy the assertion above without the bound ever binding, and this leg "
            + $"answered {adapted.Describe()}");
    }

    private static string UnboundCreateBody(Party owner) => Serialize(CommandsFor(owner));

    private static string BoundCreateBody(Party owner)
    {
        var commands = CommandsFor(owner);
        commands.MinLedgerTimeRel = RelativeBound;
        return Serialize(commands);
    }

    private static string SpecDeclaredBoundCreateBody(Party owner)
    {
        var body = JsonNode.Parse(BoundCreateBody(owner))!;
        body[MinLedgerTimeRelProperty] = RelativeBound;
        return body.ToJsonString();
    }

    private static Raw.Commands CommandsFor(Party owner) => RestCommandBuilder.BuildCommands(
        CommandsSubmission.Single(CreateCommand.For(new Marker(owner)), owner), userId: null);

    private static string Serialize(Raw.Commands commands) =>
        JsonSerializer.Serialize(commands, RestRefitSettings.SerializerOptions);

    private static JsonNode? BoundOf(string body) => JsonNode.Parse(body)![MinLedgerTimeRelProperty];

    private static async Task<SubmissionOutcome> SubmitAsync(
        HttpClient wireClient, string body, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ObservationWindow);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        var started = Stopwatch.GetTimestamp();
        try
        {
            using var response = await wireClient.PostAsync(SubmitAndWaitPath, content, deadline.Token);
            return new SubmissionOutcome(response.StatusCode, Stopwatch.GetElapsedTime(started));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SubmissionOutcome(null, Stopwatch.GetElapsedTime(started));
        }
    }

    private sealed record SubmissionOutcome(HttpStatusCode? Status, TimeSpan Elapsed)
    {
        internal bool Committed => Status == HttpStatusCode.OK;

        internal string Describe() => Status is { } status
            ? $"{(int)status} after {Elapsed.TotalSeconds:F1}s"
            : $"no response within {Elapsed.TotalSeconds:F1}s";
    }
}
