// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Diagnostics;
using Canton.Ledger.Kernel.Authentication;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using Status = Grpc.Core.Status;

namespace Canton.Ledger.Grpc.Client.Tests;

public class LedgerClientActivityEnrichmentTests
{
    private static readonly Party ActAs = new("party::alice");

    private readonly LedgerClientOptions _options;
    private readonly GrpcChannel _channel;
    private readonly CommandService.CommandServiceClient _commandService;
    private readonly StateService.StateServiceClient _stateService;
    private readonly ITokenProvider _tokenProvider = new StaticTokenProvider("test-token");

    public LedgerClientActivityEnrichmentTests()
    {
        _options = new LedgerClientOptions { GrpcAddress = "https://localhost:5001" };
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);

        var callInvoker = Substitute.For<CallInvoker>();
        _commandService = Substitute.ForPartsOf<CommandService.CommandServiceClient>(callInvoker);
        _stateService = Substitute.ForPartsOf<StateService.StateServiceClient>(callInvoker);
    }

    private LedgerClient CreateClient() => new(_options, _channel, _commandService, _tokenProvider);

    private LedgerClient CreateClientWithStateService() => new(
        _options,
        _channel,
        _commandService,
        new UpdateService.UpdateServiceClient(_channel),
        _stateService,
        tokenProvider: _tokenProvider);

    private static RuntimeCommands.ExerciseCommand ArchiveCommand(string contractId) =>
        new(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            new ContractId<LedgerClientTests.TestTemplate>(contractId),
            new RuntimeCommands.ChoiceName("Archive"),
            DamlUnit.Instance);

    private static string UniqueContractId() => $"00{Guid.NewGuid():N}";

    private static (ActivityListener Listener, ConcurrentQueue<Activity> SharedActivities) ListenTo(string sourceName)
    {
        var activities = new ConcurrentQueue<Activity>();
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activities.Enqueue
        };
        return (listener, activities);
    }

    [Fact]
    public async Task TryExerciseAsync_tags_the_activity_with_grpc_semconv_and_daml_attributes()
    {
        var contractId = UniqueContractId();
        var transaction = new Transaction { UpdateId = "update-1", Offset = 1L };
        transaction.Events.Add(new Event
        {
            Exercised = new Com.Daml.Ledger.Api.V2.ExercisedEvent
            {
                ContractId = contractId,
                TemplateId = new Com.Daml.Ledger.Api.V2.Identifier
                {
                    PackageId = "pkg", ModuleName = "Module", EntityName = "Template"
                },
                Choice = "Archive",
                ChoiceArgument = new Value { Unit = new Google.Protobuf.WellKnownTypes.Empty() },
                ExerciseResult = new Value { Unit = new Google.Protobuf.WellKnownTypes.Empty() },
                Consuming = true,
                ActingParties = { "party::alice" },
                WitnessParties = { "party::alice" },
            }
        });
        var response = new SubmitAndWaitForTransactionResponse { Transaction = transaction };
        _commandService
            .SubmitAndWaitForTransactionAsync(
                Arg.Any<SubmitAndWaitForTransactionRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AsyncUnaryCall<SubmitAndWaitForTransactionResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));

        var (listener, sharedActivities) = ListenTo(LedgerClient.ActivitySourceName);
        using var _ = listener;
        ActivitySource.AddActivityListener(listener);

        var client = CreateClient();
        await client.TryExerciseAsync<DamlUnit>(
            ArchiveCommand(contractId), ActAs, cancellationToken: TestContext.Current.CancellationToken);

        var activity = sharedActivities.Should()
            .ContainSingle(a => a.GetTagItem(LedgerClientActivityTags.DamlContractId) as string == contractId)
            .Subject;
        activity.Kind.Should().Be(ActivityKind.Client);
        activity.GetTagItem(ActivityHelper.RpcSystem).Should().Be("grpc");
        activity.GetTagItem(ActivityHelper.RpcService).Should().Be("com.daml.ledger.api.v2.CommandService");
        activity.GetTagItem(ActivityHelper.RpcMethod).Should().Be("SubmitAndWaitForTransaction");
        activity.GetTagItem(ActivityHelper.ServerAddress).Should().Be("localhost");
        activity.GetTagItem(ActivityHelper.ServerPort).Should().Be(5001);
        activity.GetTagItem(LedgerClientActivityTags.DamlChoice).Should().Be("Archive");
        activity.GetTagItem(LedgerClientActivityTags.CantonSubmitterActAs).Should().Be("party::alice");
    }

    [Fact]
    public async Task TryExerciseAsync_records_DamlError_as_an_activity_error()
    {
        var contractId = UniqueContractId();
        var errorId = $"CONTRACT_NOT_FOUND-{Guid.NewGuid():N}";
        var ex = LedgerClientTestFixtures.MakeDamlRpcException(
            errorId, "contract not found", "InvalidGivenCurrentSystemStateOther");
        LedgerClientTestFixtures.StubCommandServiceFailure(_commandService, ex);

        var (listener, sharedActivities) = ListenTo(LedgerClient.ActivitySourceName);
        using var _ = listener;
        ActivitySource.AddActivityListener(listener);

        var client = CreateClient();
        await client.TryExerciseAsync<object>(
            ArchiveCommand(contractId), ActAs, cancellationToken: TestContext.Current.CancellationToken);

        var activity = sharedActivities.Should()
            .ContainSingle(a => a.GetTagItem(LedgerClientActivityTags.DamlContractId) as string == contractId)
            .Subject;
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.GetTagItem(ActivityHelper.ErrorType).Should().Be(errorId);
    }

    [Fact]
    public async Task TryExerciseAsync_records_InfraError_as_an_activity_error()
    {
        var contractId = UniqueContractId();
        var ex = new RpcException(new Status(StatusCode.Unavailable, $"network down {Guid.NewGuid()}"));
        LedgerClientTestFixtures.StubCommandServiceFailure(_commandService, ex);

        var (listener, sharedActivities) = ListenTo(LedgerClient.ActivitySourceName);
        using var _ = listener;
        ActivitySource.AddActivityListener(listener);

        var client = CreateClient();
        await client.TryExerciseAsync<object>(
            ArchiveCommand(contractId), ActAs, cancellationToken: TestContext.Current.CancellationToken);

        var activity = sharedActivities.Should()
            .ContainSingle(a => a.GetTagItem(LedgerClientActivityTags.DamlContractId) as string == contractId)
            .Subject;
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.GetTagItem(ActivityHelper.ErrorType).Should().Be(StatusCode.Unavailable.ToString());
        activity.GetTagItem(ActivityHelper.RpcGrpcStatusCode).Should().Be((int)StatusCode.Unavailable);
    }

    [Fact]
    public async Task GetLedgerEndAsync_tags_the_activity_with_grpc_semconv_and_canton_offset()
    {
        var offset = Random.Shared.NextInt64(1_000_000, 2_000_000);
        _stateService
            .GetLedgerEndAsync(
                Arg.Any<GetLedgerEndRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AsyncUnaryCall<GetLedgerEndResponse>(
                Task.FromResult(new GetLedgerEndResponse { Offset = offset }),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));

        var (listener, sharedActivities) = ListenTo(LedgerClient.ActivitySourceName);
        using var _ = listener;
        ActivitySource.AddActivityListener(listener);

        var client = CreateClientWithStateService();
        await client.GetLedgerEndAsync(TestContext.Current.CancellationToken);

        var activity = sharedActivities.Should()
            .ContainSingle(a => a.GetTagItem(LedgerClientActivityTags.CantonOffset) as long? == offset)
            .Subject;
        activity.Kind.Should().Be(ActivityKind.Client);
        activity.GetTagItem(ActivityHelper.RpcSystem).Should().Be("grpc");
        activity.GetTagItem(ActivityHelper.RpcService).Should().Be("com.daml.ledger.api.v2.StateService");
        activity.GetTagItem(ActivityHelper.RpcMethod).Should().Be("GetLedgerEnd");
    }

    [Fact]
    public async Task GetLedgerEndAsync_records_an_RpcException_as_an_activity_error()
    {
        var detail = $"network down {Guid.NewGuid()}";
        var ex = new RpcException(new Status(StatusCode.Unavailable, detail));
        _stateService
            .GetLedgerEndAsync(
                Arg.Any<GetLedgerEndRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AsyncUnaryCall<GetLedgerEndResponse>(
                Task.FromException<GetLedgerEndResponse>(ex),
                Task.FromResult(new Metadata()),
                () => ex.Status,
                () => new Metadata(),
                () => { }));

        var (listener, sharedActivities) = ListenTo(LedgerClient.ActivitySourceName);
        using var _ = listener;
        ActivitySource.AddActivityListener(listener);

        var client = CreateClientWithStateService();

        var act = () => client.GetLedgerEndAsync(TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<RpcException>();

        var activity = sharedActivities.Should().ContainSingle(a => a.StatusDescription == detail).Subject;
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.GetTagItem(ActivityHelper.ErrorType).Should().Be(StatusCode.Unavailable.ToString());
    }
}
