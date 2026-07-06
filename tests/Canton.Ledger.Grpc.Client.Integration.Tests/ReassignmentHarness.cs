// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Kernel.Authentication;
using Com.Daml.Ledger.Api.V2;
using Com.Daml.Ledger.Api.V2.Admin;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Peaceful.Canton.Localnet.Testing;
using Richtypes;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Grpc.Client.Integration.Tests;

/// <summary>
/// Arrange harness for reassignment conformance tests on the multi-synchronizer LocalNet lane:
/// hosts one party on both synchronizers, creates an <see cref="Asset"/> on the source
/// synchronizer, unassigns it source→target via the raw <c>SubmitReassignment</c> gRPC stub, and
/// observes the resulting <see cref="UnassignedEvent"/> on a caller-supplied reassignment
/// <see cref="EventFormat"/>. The reassignment write and read paths go through raw generated stubs
/// because no typed client surface exists for them yet (ADR 0003, ADR 0007).
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
        + "on both synchronizers (LocalNet app-synchronizer.sc bootstrap, #152 finding) is required to run "
        + "this conformance spike.";

    private readonly LocalnetFixture _fixture;
    private readonly ITokenProvider _tokenProvider;
    private readonly string _userId;
    private readonly LedgerClient _client;
    private readonly AdminClient _admin;
    private readonly GrpcChannel _channel;
    private readonly CommandSubmissionService.CommandSubmissionServiceClient _submission;
    private readonly UpdateService.UpdateServiceClient _updates;
    private readonly PackageManagementService.PackageManagementServiceClient _packages;

    private ReassignmentHarness(LocalnetFixture fixture, string grpcAddress)
    {
        _fixture = fixture;
        _userId = fixture.ValidatorUserId;
        _tokenProvider = new LocalnetTokenProvider(fixture.TokenProvider.GetAccessTokenAsync);

        var options = new LedgerClientOptions { GrpcAddress = grpcAddress, UserId = _userId };
        _client = new LedgerClient(options, _tokenProvider);
        _admin = new AdminClient(options, _tokenProvider);
        _channel = GrpcChannel.ForAddress(grpcAddress);
        _submission = new CommandSubmissionService.CommandSubmissionServiceClient(_channel);
        _updates = new UpdateService.UpdateServiceClient(_channel);
        _packages = new PackageManagementService.PackageManagementServiceClient(_channel);
    }

    public static ReassignmentHarness FromFixture(LocalnetFixture fixture)
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

        var outcome = await _client.TrySubmitAndWaitForTransactionAsync(submission, cancellationToken);
        var result = Assert.IsType<ExerciseOutcome<TransactionResult>.One>(outcome).Result;
        return Assert.Single(result.CreatedContracts).ContractId;
    }

    public Task<long> LedgerEndAsync(CancellationToken cancellationToken) =>
        _client.GetLedgerEndAsync(cancellationToken);

    public async Task UnassignAsync(
        Party submitter,
        string contractId,
        string sourceSynchronizerId,
        string targetSynchronizerId,
        CancellationToken cancellationToken)
    {
        var request = new SubmitReassignmentRequest
        {
            ReassignmentCommands = new ReassignmentCommands
            {
                UserId = _userId,
                CommandId = Guid.NewGuid().ToString(),
                SubmissionId = Guid.NewGuid().ToString(),
                Submitter = submitter.Id,
                Commands =
                {
                    new ReassignmentCommand
                    {
                        UnassignCommand = new UnassignCommand
                        {
                            ContractId = contractId,
                            Source = sourceSynchronizerId,
                            Target = targetSynchronizerId,
                        },
                    },
                },
            },
        };

        try
        {
            await _submission.SubmitReassignmentAsync(
                request, await HeadersAsync(cancellationToken), deadline: null, cancellationToken: cancellationToken);
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

    public async Task<UnassignedEvent?> ObserveUnassignedAsync(
        EventFormat reassignmentFormat,
        long beginExclusiveOffset,
        string contractId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var request = new GetUpdatesRequest
        {
            BeginExclusive = beginExclusiveOffset,
            UpdateFormat = new UpdateFormat { IncludeReassignments = reassignmentFormat },
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
                if (response.UpdateCase != GetUpdatesResponse.UpdateOneofCase.Reassignment) continue;

                foreach (var reassignmentEvent in response.Reassignment.Events)
                {
                    if (reassignmentEvent.EventCase == ReassignmentEvent.EventOneofCase.Unassigned
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
