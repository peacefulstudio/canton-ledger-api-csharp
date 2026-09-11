// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Peaceful.Canton.Localnet.Testing;
using RichTypes;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Integration.Tests;

/// <summary>
/// LocalNet conformance coverage that a unit-returning choice reaches the untyped decode path as the
/// bare <c>{}</c> <see cref="WireUnitEncoding"/> exists to read. The consuming <c>Archive</c> choice
/// on the shared <c>richtypes.dar</c> <c>Marker</c> returns unit, and
/// <c>POST /v2/commands/submit-and-wait-for-transaction</c> serves its result as an
/// <c>exerciseResult</c> that is present, an object, and empty.
/// <para>
/// The delta leg asserts that emptiness on the served bytes: the participant sends nothing that
/// separates unit from a record with no fields or an empty <c>TextMap</c>, which is the whole reason
/// the untyped path cannot resolve the three apart and must adapt. It is the leg that starts failing
/// the day the participant tags the arm — as <c>{"unit":{}}</c>, the form
/// <see cref="RestValueEncoder"/> already writes and <see cref="RestValueDecoder"/> already reads as
/// a recognised sum case — at which point the transform can retire.
/// </para>
/// <para>
/// The adapted leg feeds those same served bytes through the deserializer the client reads replies
/// with and through <see cref="RestTransactionResultProjector.Project"/>, so what is measured is the
/// mapping a caller actually gets rather than a hand-written approximation of the wire shape. The
/// submission is built through the same builder and serializer options the client submits with, for
/// the same reason.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
public class RestUnitExerciseResultConformanceTests
{
    private const string SubmitAndWaitForTransactionPath = "/v2/commands/submit-and-wait-for-transaction";
    private const string ExercisedEventProperty = "ExercisedEvent";
    private const string ExerciseResultProperty = "exerciseResult";

    private static string DarPath() => Path.Combine(
        AppContext.BaseDirectory, "testdata", "richtypes", "richtypes.dar");

    private static async Task<Party> NewOwnerAsync(RestConformanceLane lane, CancellationToken cancellationToken)
    {
        var darOutcome = await lane.Fixture.UploadDarAsync(DarPath(), cancellationToken);
        Assert.True(
            darOutcome is DarUploadOutcome.Uploaded or DarUploadOutcome.AlreadyKnown,
            $"Unexpected DAR upload outcome: {darOutcome}");

        var party = await lane.Fixture.AllocatePartyAsync(
            "rest-unit-exercise-result", cancellationToken: cancellationToken);
        await lane.GrantActAsAsync(party.PartyId, cancellationToken);
        return new Party(party.PartyId);
    }

    [Fact]
    [Trait("Retires", nameof(WireUnitEncoding))]
    public async Task A_unit_returning_choice_is_served_as_a_bare_empty_object_and_adapts_to_DamlUnit()
    {
        await using var lane = await RestConformanceLane.OpenAsync(TestContext.Current.CancellationToken);
        var owner = await NewOwnerAsync(lane, TestContext.Current.CancellationToken);
        using var wireClient = lane.CreateWireLevelClient();

        var createOutcome = await lane.LedgerClient.TryCreateAsync(
            new Marker(owner), owner, cancellationToken: TestContext.Current.CancellationToken);
        var markerCid = Assert.IsType<ExerciseOutcome<ContractId<Marker>>.One>(createOutcome).Result;

        var served = await ArchiveAsync(wireClient, markerCid, owner, TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(served);
        var exerciseResult = ExerciseResultOf(document.RootElement);

        exerciseResult.ValueKind.Should().Be(
            JsonValueKind.Object,
            "the participant serves a unit choice result as an object rather than omitting it or "
            + $"naming the arm, and it served {exerciseResult.GetRawText()}");
        exerciseResult.EnumerateObject().Should().BeEmpty(
            "the served object carries nothing separating unit from a record with no fields or an "
            + "empty TextMap, which is what the untyped decode path has to adapt; the day the "
            + "participant tags the arm this leg fails and WireUnitEncoding retires, and today it "
            + $"served {exerciseResult.GetRawText()}");

        var reply = JsonSerializer.Deserialize<Raw.SubmitAndWaitForTransactionResponse>(
            served, RestRefitSettings.SerializerOptions)!;

        RestTransactionResultProjector.Project(reply.Transaction)
            .ExercisedEvents.Should().ContainSingle()
            .Which.ExerciseResult.Should().Be(
                DamlUnit.Instance,
                "the untyped path carries no Daml type to resolve the bare empty object with, so "
                + "WireUnitEncoding must map the served shape to unit rather than refusing it");
    }

    private static async Task<string> ArchiveAsync(
        HttpClient wireClient, ContractId<Marker> markerCid, Party owner, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(
            new Raw.SubmitAndWaitForTransactionRequest
            {
                Commands = RestCommandBuilder.BuildCommands(
                    CommandsSubmission.Single(
                        ExerciseCommand.For(markerCid, Marker.ChoiceArchive.Name, DamlRecord.Create()), owner),
                    userId: null),
                TransactionFormat = RestSubscribeRequestBuilder.BuildTransactionFormat(owner),
            },
            RestRefitSettings.SerializerOptions);

        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await wireClient.PostAsync(
            SubmitAndWaitForTransactionPath, content, cancellationToken);
        var served = await response.Content.ReadAsStringAsync(cancellationToken);

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "the participant must accept a consuming Archive for the served shape to mean anything, "
            + $"but it answered {(int)response.StatusCode} {served}");
        return served;
    }

    private static JsonElement ExerciseResultOf(JsonElement reply)
    {
        var exercised = reply.GetProperty("transaction").GetProperty("events").EnumerateArray()
            .Where(evt => evt.TryGetProperty(ExercisedEventProperty, out _))
            .Select(evt => evt.GetProperty(ExercisedEventProperty))
            .Should().ContainSingle().Subject;

        exercised.TryGetProperty(ExerciseResultProperty, out var exerciseResult).Should().BeTrue(
            $"the participant must serve the choice result it committed, but it served {exercised.GetRawText()}");
        return exerciseResult;
    }
}
