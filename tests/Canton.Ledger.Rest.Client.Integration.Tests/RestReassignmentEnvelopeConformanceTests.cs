// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Peaceful.Canton.Localnet.Testing;
using Richtypes;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using UnassignCommand = Canton.Ledger.Abstractions.UnassignCommand;

namespace Canton.Ledger.Rest.Client.Integration.Tests;

/// <summary>
/// LocalNet conformance coverage for the envelope a reassignment submission reaches the participant
/// in. The participant treats the <c>command</c> wrapper as mandatory and answers a submission
/// without it with <c>MISSING_FIELD</c>, so every reassignment used to be rejected before any
/// reassignment semantics ran. The acceptance criterion here is that rejection disappearing, not a
/// successful reassignment: this lane connects one synchronizer, so source and target are the same
/// and the unassign cannot succeed on it.
/// <para>
/// The two facts are one measurement. On its own, the rejection vanishing from the real submission
/// is equally consistent with the participant having stopped emitting that message at all, so the
/// second fact submits a deliberately empty command at the wire level and pins that the participant
/// still answers it exactly that way.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
public class RestReassignmentEnvelopeConformanceTests(ITestOutputHelper output)
{
    private const string MissingCommandRejection = "missing a mandatory field: command";

    private const string SubmitReassignmentPath = "/v2/commands/async/submit-reassignment";

    private static string DarPath() => Path.Combine(
        AppContext.BaseDirectory, "testdata", "richtypes", "richtypes.dar");

    [Fact]
    public async Task SubmitReassignmentAsync_is_no_longer_rejected_for_a_missing_command_field()
    {
        await using var lane = await RestConformanceLane.OpenAsync(TestContext.Current.CancellationToken);
        var owner = await NewMarkerOwnerAsync(lane, TestContext.Current.CancellationToken);
        var contractId = await ContractIdToUnassignAsync(lane, owner, TestContext.Current.CancellationToken);
        var connected = await lane.LedgerClient.GetConnectedSynchronizersAsync(
            owner, cancellationToken: TestContext.Current.CancellationToken);
        var synchronizer = new SynchronizerId(connected.Should().ContainSingle().Subject.SynchronizerId);

        var act = () => lane.LedgerClient.SubmitReassignmentAsync(
            ReassignmentSubmission.Of(new UnassignCommand(contractId, synchronizer, synchronizer), owner),
            TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<LedgerOperationException>();
        output.WriteLine($"Participant answered: {thrown.Which.Message}");
        thrown.Which.Message.Should().NotContain(MissingCommandRejection);
    }

    [Fact]
    public async Task SubmitReassignment_still_reports_a_missing_command_field_for_an_empty_command()
    {
        await using var lane = await RestConformanceLane.OpenAsync(TestContext.Current.CancellationToken);
        var submitter = await NewPartyAsync(lane, "rest-reassignment-control", TestContext.Current.CancellationToken);
        using var client = lane.CreateWireLevelClient();
        using var content = new StringContent(
            $$$"""
            {"reassignmentCommands":{"commandId":"{{{Guid.NewGuid()}}}","submitter":"{{{submitter.Id}}}","commands":[{}]}}
            """,
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync(
            SubmitReassignmentPath, content, TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        output.WriteLine($"Participant answered {(int)response.StatusCode}: {body}");
        response.IsSuccessStatusCode.Should().BeFalse();
        body.Should().Contain(MissingCommandRejection);
    }

    private static async Task<Party> NewMarkerOwnerAsync(
        RestConformanceLane lane, CancellationToken cancellationToken)
    {
        var darOutcome = await lane.Fixture.UploadDarAsync(DarPath(), cancellationToken);
        darOutcome.Should().BeOneOf(DarUploadOutcome.Uploaded, DarUploadOutcome.AlreadyKnown);

        return await NewPartyAsync(lane, "rest-reassignment-envelope", cancellationToken);
    }

    private static async Task<Party> NewPartyAsync(
        RestConformanceLane lane, string partyIdHint, CancellationToken cancellationToken)
    {
        var party = await lane.Fixture.AllocatePartyAsync(partyIdHint, cancellationToken: cancellationToken);
        await lane.Fixture.GrantUserRightsAsync(
            lane.Fixture.ValidatorUserId, actAs: [party.PartyId], cancellationToken: cancellationToken);
        return new Party(party.PartyId);
    }

    private async Task<string> ContractIdToUnassignAsync(
        RestConformanceLane lane, Party owner, CancellationToken cancellationToken)
    {
        var outcome = await lane.LedgerClient.TryCreateAsync(
            new Marker(owner),
            new RuntimeCommands.SubmitterInfo(new HashSet<Party> { owner }, new HashSet<Party>()),
            cancellationToken: cancellationToken);

        if (outcome is ExerciseOutcome<ContractId<Marker>>.One created)
        {
            output.WriteLine(
                $"Unassigning the contract just created on the ledger: {created.Result.Value}");
            return created.Result.Value;
        }

        var absent = AbsentContractId();
        output.WriteLine(
            $"Creating a contract yielded {outcome.GetType().Name} instead of a contract id, so unassigning "
            + $"the well-formed but absent {absent}. A contract-lookup failure is not a missing-field rejection, "
            + "so the tell still holds.");
        return absent;
    }

    private static string AbsentContractId() => $"00{Guid.NewGuid():N}{Guid.NewGuid():N}";
}
