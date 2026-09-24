// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Peaceful.Canton.Localnet.Testing;
using Canton.Ledger.Grpc.Client.Integration.Tests;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Xunit;

namespace Canton.Ledger.Rest.Client.Integration.Tests;

/// <summary>
/// Live conformance for an ACS snapshot larger than the participant's
/// <c>http-list-max-elements-limit</c>, which defaults to 200: the snapshot must arrive whole,
/// read page by page over <c>POST /v2/state/active-contracts-page</c>, where one un-paged read of
/// it is answered with <c>413 Content Too Large</c>.
/// </summary>
[Trait("Category", "Integration")]
public class RestActiveContractsPagingConformanceTests
{
    private const int CreatesPerCommand = 30;
    private const int Commands = 7;
    private const int SeededMarkers = 210;
    private const int MaxRetries = 9;

    [Fact]
    public async Task SubscribeActiveAsync_reads_a_snapshot_above_the_participant_list_limit_whole()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await RestConformanceLane.OpenAsync(cancellationToken);
        var owner = await NewOwnerAsync(lane, cancellationToken);
        for (var command = 0; command < Commands; command++)
        {
            await CreateMarkersAsync(lane, owner, cancellationToken);
        }

        var ledgerEnd = await lane.LedgerClient.GetLedgerEndAsync(cancellationToken: cancellationToken);
        var entries = new List<AcsSnapshotEntry<Marker>>();
        await foreach (var entry in lane.LedgerClient.SubscribeActiveAsync<Marker>(owner, ledgerEnd, cancellationToken))
        {
            entries.Add(entry);
        }

        entries.OfType<AcsSnapshotEntry<Marker>.StreamError>().Should().BeEmpty();
        entries.OfType<AcsSnapshotEntry<Marker>.Created>()
            .Select(created => created.ContractId.Value)
            .Distinct()
            .Should().HaveCount(SeededMarkers);
        entries[^1].Should().BeOfType<AcsSnapshotEntry<Marker>.Checkpoint>();
    }

    private static async Task<Party> NewOwnerAsync(RestConformanceLane lane, CancellationToken cancellationToken)
    {
        var darOutcome = await lane.Fixture.UploadDarAsync(RichTypesDar.Path, cancellationToken);
        Assert.True(
            darOutcome is DarUploadOutcome.Uploaded or DarUploadOutcome.AlreadyKnown,
            $"Unexpected DAR upload outcome: {darOutcome}");

        var party = await lane.Fixture.AllocatePartyAsync("rest-acs-paging", cancellationToken: cancellationToken);
        await lane.GrantActAsAsync(party.PartyId, cancellationToken);
        return new Party(party.PartyId);
    }

    private static async Task CreateMarkersAsync(RestConformanceLane lane, Party owner, CancellationToken cancellationToken)
    {
        var creates = Enumerable.Range(0, CreatesPerCommand)
            .Select(ICommand (_) => CreateCommand.For(new Marker(owner)))
            .ToArray();
        var submission = CommandsSubmission.Multiple(creates).WithActAs(owner);
        for (var attempt = 0; ; attempt++)
        {
            var outcome = await lane.LedgerClient.TrySubmitAndWaitForTransactionAsync(
                submission.WithCommandId(new CommandId(Guid.NewGuid().ToString())),
                cancellationToken: cancellationToken);
            switch (outcome)
            {
                case ExerciseOutcome<TransactionResult>.DamlError error
                    when IsRetryable(error.Category) && attempt < MaxRetries:
                    await Task.Delay(TimeSpan.FromMilliseconds(250 << attempt), cancellationToken);
                    break;
                case ExerciseOutcome<TransactionResult>.DamlError or ExerciseOutcome<TransactionResult>.InfraError:
                    Assert.Fail($"seeding {CreatesPerCommand} markers did not commit: {outcome}");
                    return;
                default:
                    return;
            }
        }
    }

    private static bool IsRetryable(DamlErrorCategory category) =>
        category is DamlErrorCategory.ContentionOnSharedResources or DamlErrorCategory.TransientServerFailure;
}
