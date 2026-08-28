// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Rest.Client.Raw;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Richtypes;
using Xunit;
using AssignCommand = Canton.Ledger.Abstractions.AssignCommand;
using RuntimeCommands = Daml.Runtime.Commands;
using UnassignCommand = Canton.Ledger.Abstractions.UnassignCommand;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Integration.Tests;

/// <summary>
/// Arrange + act harness for REST reassignment conformance on the multi-synchronizer LocalNet lane:
/// discovers two connected synchronizers, vets <c>richtypes.dar</c> on both, hosts one party on both,
/// creates an <see cref="Asset"/> on the source synchronizer, and drives the unassign source→assign
/// target round trip through <see cref="RestLedgerClient"/>'s two reassignment write surfaces —
/// <see cref="ICantonLedgerClient.SubmitReassignmentAsync"/> (fire, observed on the bounded
/// <see cref="RestLedgerClient.SubscribeAsync{T}"/> read) and
/// <see cref="ICantonLedgerClient.TrySubmitAndWaitForReassignmentAsync{T}"/> (submit-and-wait,
/// returning the typed reassignment directly). Per-synchronizer DAR vetting and party allocation go
/// through the raw JSON Ledger API admin surfaces (<see cref="IDarApi"/> /
/// <see cref="IPartyManagementServiceApi"/>), which accept the synchronizer pin the shared LocalNet
/// fixture's <c>UploadDarAsync</c>/<c>AllocatePartyAsync</c> convenience helpers do not expose —
/// mirroring the gRPC harness's per-synchronizer <c>UploadDarFile</c>/<c>AllocateParty</c>. The
/// single-synchronizer and reassignment-feature-disabled layers turn into
/// <see cref="Assert.Skip(string)"/> rather than failures, mirroring the gRPC lane's guarded-skip.
/// </summary>
internal sealed class RestReassignmentHarness
{
    private const string ReassignmentFeatureDisabledSignal = "Multi-synchronizer feature flag is not enabled";

    internal const string SingleSyncSkipMessage =
        "Skipping: the participant reports fewer than two connected synchronizers, so this is the "
        + "single-synchronizer lane. Bring up a multi-synchronizer participant to run this "
        + "reassignment conformance test.";

    internal const string ReassignmentFeatureDisabledSkipMessage =
        "Skipping: the participant rejected the reassignment with \"Multi-synchronizer feature flag "
        + "is not enabled\". The multi-sync LocalNet connects the validator to two synchronizers but "
        + "does not set the EnableMultiSynchronizer participant topology feature flag, so "
        + "cross-synchronizer reassignment is disabled at submission. Baking that flag into the "
        + "LocalNet bootstrap is a known follow-up.";

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly RestConformanceLane _lane;

    internal RestReassignmentHarness(RestConformanceLane lane) => _lane = lane;

    internal async Task<SynchronizerPair> DiscoverSynchronizerPairAsync(CancellationToken cancellationToken)
    {
        var synchronizers = await _lane.LedgerClient.GetConnectedSynchronizersAsync(
            cancellationToken: cancellationToken);
        if (synchronizers.Count < 2)
        {
            Assert.Skip(SingleSyncSkipMessage);
        }

        return new SynchronizerPair(
            new SynchronizerId(synchronizers[0].SynchronizerId),
            new SynchronizerId(synchronizers[1].SynchronizerId));
    }

    internal async Task VetRichTypesDarOnBothAsync(
        string darPath, SynchronizerPair synchronizers, CancellationToken cancellationToken)
    {
        var darFile = await File.ReadAllBytesAsync(darPath, cancellationToken);
        var dars = _lane.Api<IDarApi>();

        foreach (var synchronizerId in new[] { synchronizers.Source.Id, synchronizers.Target.Id })
        {
            await dars.UploadDar(
                new MemoryStream(darFile),
                vetAllPackages: true,
                synchronizerId,
                cancellationToken);
        }
    }

    internal async Task<Party> HostPartyOnBothSynchronizersAsync(
        string partyIdHint, SynchronizerPair synchronizers, CancellationToken cancellationToken)
    {
        var uniquePartyIdHint = $"{partyIdHint}-{Guid.NewGuid():N}";
        var parties = _lane.Api<IPartyManagementServiceApi>();

        var onSource = await parties.AllocateParty(
            new AllocatePartyRequest { PartyIdHint = uniquePartyIdHint, SynchronizerId = synchronizers.Source.Id },
            cancellationToken);
        var onTarget = await parties.AllocateParty(
            new AllocatePartyRequest { PartyIdHint = uniquePartyIdHint, SynchronizerId = synchronizers.Target.Id },
            cancellationToken);

        var sourceParty = onSource.PartyDetails.Party;
        var targetParty = onTarget.PartyDetails.Party;
        if (!string.Equals(sourceParty, targetParty, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Hosting one party on both synchronizers failed: allocating hint '{uniquePartyIdHint}' pinned to "
                + $"'{synchronizers.Source.Id}' produced party '{sourceParty}', but pinning the same hint to "
                + $"'{synchronizers.Target.Id}' produced the distinct party '{targetParty}'. A cross-synchronizer "
                + "unassign requires a single party hosted on both synchronizers.");
        }

        var party = new Party(sourceParty);
        await _lane.Fixture.GrantUserRightsAsync(
            _lane.Fixture.ValidatorUserId, actAs: [party.Id], cancellationToken: cancellationToken);

        var hosted = await _lane.LedgerClient.GetConnectedSynchronizersAsync(party, cancellationToken: cancellationToken);
        if (hosted.Count < 2)
        {
            throw new InvalidOperationException(
                $"Party '{party.Id}' is hosted on {hosted.Count} synchronizer(s) after pinning to both "
                + $"'{synchronizers.Source.Id}' and '{synchronizers.Target.Id}'; a cross-synchronizer unassign "
                + "requires it to be hosted on both.");
        }

        return party;
    }

    internal async Task<string> CreateAssetAsync(
        Party issuer,
        SynchronizerId synchronizerId,
        decimal amount,
        CancellationToken cancellationToken)
    {
        var submission = RuntimeCommands.CommandsSubmission
            .Single(RuntimeCommands.CreateCommand.For(new Asset(issuer, amount)))
            .WithActAs(issuer)
            .WithSynchronizerId(synchronizerId)
            .WithCommandId(new RuntimeCommands.CommandId(Guid.NewGuid().ToString()));

        var outcome = await _lane.LedgerClient.TrySubmitAndWaitForTransactionAsync(
            submission, cancellationToken: cancellationToken);
        var result = Assert.IsType<ExerciseOutcome<TransactionResult>.One>(outcome).Result;
        return Assert.Single(result.CreatedContracts).ContractId;
    }

    internal async Task<long> LedgerEndAsync(CancellationToken cancellationToken) =>
        (await _lane.LedgerClient.GetLedgerEndAsync(cancellationToken: cancellationToken)).Value;

    internal async Task UnassignAsync(
        Party submitter,
        string contractId,
        SynchronizerPair synchronizers,
        CancellationToken cancellationToken)
    {
        var submission = ReassignmentSubmission.Of(
            new UnassignCommand(contractId, synchronizers.Source, synchronizers.Target), submitter);

        try
        {
            await _lane.LedgerClient.SubmitReassignmentAsync(submission, cancellationToken);
        }
        catch (LedgerOperationException ex) when (IsReassignmentFeatureDisabled(ex.Message))
        {
            Assert.Skip(ReassignmentFeatureDisabledSkipMessage);
        }
    }

    internal async Task AssignAsync(
        Party submitter,
        string reassignmentId,
        SynchronizerPair synchronizers,
        CancellationToken cancellationToken)
    {
        var submission = ReassignmentSubmission.Of(
            new AssignCommand(reassignmentId, synchronizers.Source, synchronizers.Target), submitter);

        try
        {
            await _lane.LedgerClient.SubmitReassignmentAsync(submission, cancellationToken);
        }
        catch (LedgerOperationException ex) when (IsReassignmentFeatureDisabled(ex.Message))
        {
            Assert.Skip(ReassignmentFeatureDisabledSkipMessage);
        }
    }

    internal Task<ContractStreamEvent<Asset>.Unassigned?> ObserveUnassignedAsync(
        Party issuer,
        long beginExclusiveOffset,
        string contractId,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        ObserveAsync<ContractStreamEvent<Asset>.Unassigned>(
            issuer, beginExclusiveOffset, e => e.ContractId.Value == contractId, timeout, cancellationToken);

    internal Task<ContractStreamEvent<Asset>.Assigned?> ObserveAssignedAsync(
        Party issuer,
        long beginExclusiveOffset,
        string contractId,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        ObserveAsync<ContractStreamEvent<Asset>.Assigned>(
            issuer, beginExclusiveOffset, e => e.ContractId.Value == contractId, timeout, cancellationToken);

    internal async Task<ContractStreamEvent<Asset>.Unassigned> SubmitAndWaitUnassignAsync(
        Party submitter,
        string contractId,
        SynchronizerPair synchronizers,
        CancellationToken cancellationToken)
    {
        var submission = ReassignmentSubmission.Of(
            new UnassignCommand(contractId, synchronizers.Source, synchronizers.Target), submitter);
        var outcome = await _lane.LedgerClient.TrySubmitAndWaitForReassignmentAsync<Asset>(
            submission, cancellationToken: cancellationToken);
        return RequireVariant<ContractStreamEvent<Asset>.Unassigned>(outcome);
    }

    internal async Task<ContractStreamEvent<Asset>.Assigned> SubmitAndWaitAssignAsync(
        Party submitter,
        string reassignmentId,
        SynchronizerPair synchronizers,
        CancellationToken cancellationToken)
    {
        var submission = ReassignmentSubmission.Of(
            new AssignCommand(reassignmentId, synchronizers.Source, synchronizers.Target), submitter);
        var outcome = await _lane.LedgerClient.TrySubmitAndWaitForReassignmentAsync<Asset>(
            submission, cancellationToken: cancellationToken);
        return RequireVariant<ContractStreamEvent<Asset>.Assigned>(outcome);
    }

    private async Task<TVariant?> ObserveAsync<TVariant>(
        Party issuer,
        long beginExclusiveOffset,
        Func<TVariant, bool> matches,
        TimeSpan timeout,
        CancellationToken cancellationToken)
        where TVariant : ContractStreamEvent<Asset>
    {
        var submitter = new RuntimeCommands.SubmitterInfo(new HashSet<Party> { issuer }, new HashSet<Party>());
        var deadline = DateTime.UtcNow + timeout;
        var currentOffset = beginExclusiveOffset;

        while (DateTime.UtcNow < deadline)
        {
            var end = await LedgerEndAsync(cancellationToken);
            if (end > currentOffset)
            {
                await foreach (var streamEvent in _lane.LedgerClient.SubscribeAsync<Asset>(
                    submitter, LedgerOffset.At(currentOffset), LedgerOffset.At(end), cancellationToken))
                {
                    if (streamEvent is TVariant variant && matches(variant))
                    {
                        return variant;
                    }
                }

                currentOffset = end;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }

        Assert.Fail(
            $"Timed out after {timeout.TotalSeconds:0}s waiting for a {typeof(TVariant).Name} event matching "
            + $"the predicate (offset window [{beginExclusiveOffset}, {currentOffset}]).");
        return default;
    }

    private static TVariant RequireVariant<TVariant>(ExerciseOutcome<ContractStreamEvent<Asset>> outcome)
        where TVariant : ContractStreamEvent<Asset>
    {
        if (outcome is ExerciseOutcome<ContractStreamEvent<Asset>>.One { Result: TVariant variant })
        {
            return variant;
        }

        SkipIfReassignmentFeatureDisabled(outcome);
        throw new InvalidOperationException(
            $"Expected a {typeof(TVariant).Name} reassignment result but got: {DescribeOutcome(outcome)}");
    }

    private static void SkipIfReassignmentFeatureDisabled(ExerciseOutcome<ContractStreamEvent<Asset>> outcome)
    {
        if (outcome is ExerciseOutcome<ContractStreamEvent<Asset>>.DamlError damlError
            && IsReassignmentFeatureDisabled(damlError.Message))
        {
            Assert.Skip(ReassignmentFeatureDisabledSkipMessage);
        }
    }

    private static bool IsReassignmentFeatureDisabled(string message) =>
        message.Contains(ReassignmentFeatureDisabledSignal, StringComparison.Ordinal);

    private static string DescribeOutcome(ExerciseOutcome<ContractStreamEvent<Asset>> outcome) => outcome switch
    {
        ExerciseOutcome<ContractStreamEvent<Asset>>.One one => $"One({one.Result.GetType().Name})",
        ExerciseOutcome<ContractStreamEvent<Asset>>.DamlError error => $"DamlError({error.ErrorId}: {error.Message})",
        ExerciseOutcome<ContractStreamEvent<Asset>>.InfraError error => $"InfraError({error.StatusCode}: {error.Message})",
        _ => outcome.GetType().Name,
    };
}

internal readonly record struct SynchronizerPair(SynchronizerId Source, SynchronizerId Target);
