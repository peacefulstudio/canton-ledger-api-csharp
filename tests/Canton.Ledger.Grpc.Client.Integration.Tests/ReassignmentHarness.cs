// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing.Localnet;
using Com.Daml.Ledger.Api.V2.Admin;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Richtypes;
using Xunit;
using PeacefulLocalnet = Peaceful.Canton.Localnet.Testing;
using ProtoV2 = Com.Daml.Ledger.Api.V2;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Grpc.Client.Integration.Tests;

/// <summary>
/// Arrange harness for reassignment conformance tests on the multi-synchronizer LocalNet lane:
/// hosts one party on both synchronizers, creates an <see cref="Asset"/> on the source
/// synchronizer, unassigns it source→target through the typed
/// <see cref="ICantonLedgerClient.SubmitReassignmentAsync"/> write surface, and
/// observes the resulting <see cref="Com.Daml.Ledger.Api.V2.UnassignedEvent"/> on a caller-supplied reassignment
/// <see cref="Com.Daml.Ledger.Api.V2.EventFormat"/>. Observation reads the raw <c>UnassignedEvent</c> off
/// the wire because the conformance question is which caller-supplied <c>EventFormat</c> the participant
/// honours server-side, and the typed subscription surface derives its filter from the marker rather than
/// accepting one.
/// </summary>
internal sealed class ReassignmentHarness : IAsyncDisposable
{
    private const string GrpcUrlEnv = "CANTON_LOCALNET_A_VALIDATOR_1_GRPC_URL";
    private const string DefaultGrpcUrl = "http://localhost:11901";

    private const string ReassignmentFeatureDisabledSkipMessage =
        "Skipping: the participant rejected the unassign with \"Multi-synchronizer feature flag is not "
        + "enabled\". The --multi-sync LocalNet connects the validator to two synchronizers but does not "
        + "set the EnableMultiSynchronizer participant topology feature flag on its synchronizer trust "
        + "certificates, so cross-synchronizer reassignment is disabled at submission. Enabling that flag "
        + "on both synchronizers (LocalNet app-synchronizer.sc bootstrap) is required to run "
        + "this conformance spike.";

    private readonly PeacefulLocalnet.LocalnetFixture _fixture;
    private readonly ITokenProvider _tokenProvider;
    private readonly string _userId;
    private readonly LedgerClient _client;
    private readonly AdminClient _admin;
    private readonly GrpcChannel _channel;
    private readonly ProtoV2.UpdateService.UpdateServiceClient _updates;
    private readonly PackageManagementService.PackageManagementServiceClient _packages;

    private ReassignmentHarness(PeacefulLocalnet.LocalnetFixture fixture, string grpcAddress)
    {
        _fixture = fixture;
        _userId = fixture.ValidatorUserId;
        _tokenProvider = new LocalnetTokenProvider(fixture.TokenProvider.GetAccessTokenAsync);

        var options = new LedgerClientOptions { GrpcAddress = grpcAddress, UserId = _userId };
        _client = new LedgerClient(options, _tokenProvider);
        _admin = new AdminClient(options, _tokenProvider);
        _channel = GrpcChannel.ForAddress(grpcAddress);
        _updates = new ProtoV2.UpdateService.UpdateServiceClient(_channel);
        _packages = new PackageManagementService.PackageManagementServiceClient(_channel);
    }

    public static ReassignmentHarness FromFixture(PeacefulLocalnet.LocalnetFixture fixture)
    {
        var grpcAddress = Environment.GetEnvironmentVariable(GrpcUrlEnv) ?? DefaultGrpcUrl;
        return new ReassignmentHarness(fixture, grpcAddress);
    }

    public async Task UploadRichTypesDarAsync(CancellationToken cancellationToken)
    {
        var darFile = ByteString.CopyFrom(await File.ReadAllBytesAsync(DarPath(), cancellationToken));
        var headers = await HeadersAsync(cancellationToken);
        var synchronizers = await ParticipantSynchronizersAsync(cancellationToken);

        foreach (var synchronizer in synchronizers)
        {
            var request = new UploadDarFileRequest
            {
                DarFile = darFile,
                VettingChange = UploadDarFileRequest.Types.VettingChange.VetAllPackages,
                SynchronizerId = synchronizer.SynchronizerId,
            };
            await _packages.UploadDarFileAsync(
                request, headers, deadline: null, cancellationToken: cancellationToken);
        }
    }

    public Task<IReadOnlyList<ConnectedSynchronizer>> ParticipantSynchronizersAsync(
        CancellationToken cancellationToken) =>
        _client.GetConnectedSynchronizersAsync(cancellationToken: cancellationToken);

    public async Task<Party> HostPartyOnBothSynchronizersAsync(
        string partyIdHint,
        string sourceSynchronizerId,
        string targetSynchronizerId,
        CancellationToken cancellationToken)
    {
        var uniquePartyIdHint = $"{partyIdHint}-{Guid.NewGuid():N}";
        var onSource = await _admin.AllocatePartyAsync(
            uniquePartyIdHint, synchronizerId: sourceSynchronizerId, cancellationToken: cancellationToken);
        var onTarget = await _admin.AllocatePartyAsync(
            uniquePartyIdHint, synchronizerId: targetSynchronizerId, cancellationToken: cancellationToken);

        if (!string.Equals(onSource.Party, onTarget.Party, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Hosting one party on both synchronizers failed: allocating hint '{uniquePartyIdHint}' pinned to "
                + $"'{sourceSynchronizerId}' produced party '{onSource.Party}', but pinning the same hint to "
                + $"'{targetSynchronizerId}' produced the distinct party '{onTarget.Party}'. A cross-synchronizer "
                + "unassign requires a single party hosted on both synchronizers; the two-pinned-allocations "
                + "approach does not achieve that on this participant and a topology-level party-to-participant "
                + "mapping on the second synchronizer is needed instead.");
        }

        var party = new Party(onSource.Party);
        await _fixture.GrantUserRightsAsync(
            _userId, actAs: new[] { party.Id }, cancellationToken: cancellationToken);

        var hosted = await _client.GetConnectedSynchronizersAsync(party, cancellationToken: cancellationToken);
        if (hosted.Count < 2)
        {
            throw new InvalidOperationException(
                $"Party '{party.Id}' is hosted on {hosted.Count} synchronizer(s) after pinning to both "
                + $"'{sourceSynchronizerId}' and '{targetSynchronizerId}'; a cross-synchronizer unassign requires "
                + "it to be hosted on both.");
        }

        return party;
    }

    public async Task<string> CreateAssetAsync(
        Party issuer,
        string synchronizerId,
        decimal amount,
        CancellationToken cancellationToken)
    {
        var submission = RuntimeCommands.CommandsSubmission
            .Single(RuntimeCommands.CreateCommand.For(new Asset(issuer, amount)))
            .WithActAs(issuer)
            .WithSynchronizerId(new SynchronizerId(synchronizerId))
            .WithCommandId(new RuntimeCommands.CommandId(Guid.NewGuid().ToString()));

        var outcome = await _client.TrySubmitAndWaitForTransactionAsync(submission, cancellationToken: cancellationToken);
        var result = Assert.IsType<ExerciseOutcome<TransactionResult>.One>(outcome).Result;
        return Assert.Single(result.CreatedContracts).ContractId;
    }

    public async Task<long> LedgerEndAsync(CancellationToken cancellationToken) =>
        (await _client.GetLedgerEndAsync(cancellationToken: cancellationToken)).Value;

    public async Task UnassignAsync(
        Party submitter,
        string contractId,
        string sourceSynchronizerId,
        string targetSynchronizerId,
        CancellationToken cancellationToken)
    {
        var submission = ReassignmentSubmission.Of(
            new UnassignCommand(
                contractId,
                new SynchronizerId(sourceSynchronizerId),
                new SynchronizerId(targetSynchronizerId)),
            submitter);

        try
        {
            await _client.SubmitReassignmentAsync(submission, cancellationToken);
        }
        catch (RpcException ex) when (IsReassignmentFeatureDisabled(ex))
        {
            Assert.Skip(ReassignmentFeatureDisabledSkipMessage);
        }
    }

    public async Task AssignAsync(
        Party submitter,
        string reassignmentId,
        string sourceSynchronizerId,
        string targetSynchronizerId,
        CancellationToken cancellationToken)
    {
        var submission = ReassignmentSubmission.Of(
            new AssignCommand(
                reassignmentId,
                new SynchronizerId(sourceSynchronizerId),
                new SynchronizerId(targetSynchronizerId)),
            submitter);

        try
        {
            await _client.SubmitReassignmentAsync(submission, cancellationToken);
        }
        catch (RpcException ex) when (IsReassignmentFeatureDisabled(ex))
        {
            Assert.Skip(ReassignmentFeatureDisabledSkipMessage);
        }
    }

    private static bool IsReassignmentFeatureDisabled(RpcException ex) =>
        ex.StatusCode == StatusCode.InvalidArgument
        && ex.Status.Detail.Contains(
            "Multi-synchronizer feature flag is not enabled", StringComparison.Ordinal);

    public async Task<ProtoV2.UnassignedEvent?> ObserveUnassignedAsync(
        ProtoV2.EventFormat reassignmentFormat,
        long beginExclusiveOffset,
        string contractId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var request = new ProtoV2.GetUpdatesRequest
        {
            BeginExclusive = beginExclusiveOffset,
            UpdateFormat = new ProtoV2.UpdateFormat { IncludeReassignments = reassignmentFormat },
        };
        var headers = await HeadersAsync(cancellationToken);

        using var call = _updates.GetUpdates(
            request, headers, deadline: null, cancellationToken: linked.Token);
        linked.CancelAfter(timeout);

        try
        {
            while (await call.ResponseStream.MoveNext(linked.Token))
            {
                var response = call.ResponseStream.Current;
                if (response.UpdateCase != ProtoV2.GetUpdatesResponse.UpdateOneofCase.Reassignment) continue;

                foreach (var reassignmentEvent in response.Reassignment.Events)
                {
                    if (reassignmentEvent.EventCase == ProtoV2.ReassignmentEvent.EventOneofCase.Unassigned
                        && reassignmentEvent.Unassigned.ContractId == contractId)
                    {
                        return reassignmentEvent.Unassigned;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (TimedOut(linked, cancellationToken))
        {
            return null;
        }
        catch (RpcException) when (TimedOut(linked, cancellationToken))
        {
            return null;
        }

        return null;
    }

    public async Task<ProtoV2.AssignedEvent?> ObserveAssignedAsync(
        ProtoV2.EventFormat reassignmentFormat,
        long beginExclusiveOffset,
        string contractId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var request = new ProtoV2.GetUpdatesRequest
        {
            BeginExclusive = beginExclusiveOffset,
            UpdateFormat = new ProtoV2.UpdateFormat { IncludeReassignments = reassignmentFormat },
        };
        var headers = await HeadersAsync(cancellationToken);

        using var call = _updates.GetUpdates(
            request, headers, deadline: null, cancellationToken: linked.Token);
        linked.CancelAfter(timeout);

        try
        {
            while (await call.ResponseStream.MoveNext(linked.Token))
            {
                var response = call.ResponseStream.Current;
                if (response.UpdateCase != ProtoV2.GetUpdatesResponse.UpdateOneofCase.Reassignment) continue;

                foreach (var reassignmentEvent in response.Reassignment.Events)
                {
                    if (reassignmentEvent.EventCase == ProtoV2.ReassignmentEvent.EventOneofCase.Assigned
                        && reassignmentEvent.Assigned.CreatedEvent?.ContractId == contractId)
                    {
                        return reassignmentEvent.Assigned;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (TimedOut(linked, cancellationToken))
        {
            return null;
        }
        catch (RpcException) when (TimedOut(linked, cancellationToken))
        {
            return null;
        }

        return null;
    }

    public async Task<TypedReassignmentObservation<T>> ObserveTypedReassignmentAsync<T>(
        Party issuer,
        long fromOffset,
        string contractId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
        where T : IDamlType
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);

        var submitter = new RuntimeCommands.SubmitterInfo(
            new HashSet<Party> { issuer }, new HashSet<Party>());

        ContractStreamEvent<T>.Unassigned? unassigned = null;
        ContractStreamEvent<T>.Assigned? assigned = null;

        try
        {
            await foreach (var streamEvent in _client.SubscribeAsync<T>(submitter, LedgerOffset.At(fromOffset), cancellationToken: linked.Token))
            {
                switch (streamEvent)
                {
                    case ContractStreamEvent<T>.Unassigned u when u.ContractId.Value == contractId:
                        unassigned = u;
                        break;
                    case ContractStreamEvent<T>.Assigned a when a.ContractId.Value == contractId:
                        assigned = a;
                        break;
                }

                if (unassigned is not null && assigned is not null) break;
            }
        }
        catch (OperationCanceledException) when (TimedOut(linked, cancellationToken))
        {
        }

        return new TypedReassignmentObservation<T>(unassigned, assigned);
    }

    private static bool TimedOut(CancellationTokenSource linked, CancellationToken caller) =>
        linked.IsCancellationRequested && !caller.IsCancellationRequested;

    private async Task<Metadata> HeadersAsync(CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetTokenAsync(cancellationToken);
        return new Metadata { { "authorization", $"Bearer {token}" } };
    }

    private static string DarPath() => Path.Combine(
        AppContext.BaseDirectory, "testdata", "richtypes", "richtypes.dar");

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        _admin.Dispose();
        await _channel.ShutdownAsync();
        _channel.Dispose();
    }
}

internal sealed record TypedReassignmentObservation<T>(
    ContractStreamEvent<T>.Unassigned? Unassigned,
    ContractStreamEvent<T>.Assigned? Assigned)
    where T : IDamlType;
