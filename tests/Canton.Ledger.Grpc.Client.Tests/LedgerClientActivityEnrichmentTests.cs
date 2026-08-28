// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using Xunit;
using Interactive = Com.Daml.Ledger.Api.V2.Interactive;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using Status = Grpc.Core.Status;

namespace Canton.Ledger.Grpc.Client.Tests;

[Collection("LedgerClient global ActivitySource")]
public class LedgerClientActivityEnrichmentTests
{
    private static readonly Party ActAs = new("party::alice");

    private readonly LedgerClientOptions _options;
    private readonly GrpcChannel _channel;
    private readonly CommandService.CommandServiceClient _commandService;
    private readonly StateService.StateServiceClient _stateService;
    private readonly Interactive.InteractiveSubmissionService.InteractiveSubmissionServiceClient _interactiveSubmissionService;
    private readonly ITokenProvider _tokenProvider = new StaticTokenProvider("test-token");

    public LedgerClientActivityEnrichmentTests()
    {
        _options = new LedgerClientOptions { GrpcAddress = "https://localhost:5001" };
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);

        var callInvoker = Substitute.For<CallInvoker>();
        _commandService = Substitute.ForPartsOf<CommandService.CommandServiceClient>(callInvoker);
        _stateService = Substitute.ForPartsOf<StateService.StateServiceClient>(callInvoker);
        _interactiveSubmissionService = Substitute
            .ForPartsOf<Interactive.InteractiveSubmissionService.InteractiveSubmissionServiceClient>(callInvoker);
    }

    private LedgerClient CreateClient() => new(_options, _channel, _commandService, _tokenProvider);

    private LedgerClient CreateClientWithStateService() => new(
        _options,
        _channel,
        _commandService,
        new UpdateService.UpdateServiceClient(_channel),
        _stateService,
        tokenProvider: _tokenProvider);

    private LedgerClient CreateClientWithInteractiveSubmissionService() => new(
        _options,
        _channel,
        _commandService,
        new UpdateService.UpdateServiceClient(_channel),
        new StateService.StateServiceClient(_channel),
        new CommandSubmissionService.CommandSubmissionServiceClient(_channel),
        new CommandCompletionService.CommandCompletionServiceClient(_channel),
        _tokenProvider,
        interactiveSubmissionService: _interactiveSubmissionService);

    private static RuntimeCommands.ExerciseCommand ArchiveCommand(string contractId) =>
        new(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            new ContractId<LedgerClientTests.TestTemplate>(contractId),
            new RuntimeCommands.ChoiceName("Archive"),
            DamlUnit.Instance);

    private static string UniqueContractId() => $"00{Guid.NewGuid():N}";


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

        using var capture = ActivityCapture.Of(LedgerClient.ActivitySourceName);

        var client = CreateClient();
        await client.TryExerciseAsync<DamlUnit>(
            ArchiveCommand(contractId), ActAs, cancellationToken: TestContext.Current.CancellationToken);

        var activity = capture.Activities.Should()
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

        using var capture = ActivityCapture.Of(LedgerClient.ActivitySourceName);

        var client = CreateClient();
        await client.TryExerciseAsync<object>(
            ArchiveCommand(contractId), ActAs, cancellationToken: TestContext.Current.CancellationToken);

        var activity = capture.Activities.Should()
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

        using var capture = ActivityCapture.Of(LedgerClient.ActivitySourceName);

        var client = CreateClient();
        await client.TryExerciseAsync<object>(
            ArchiveCommand(contractId), ActAs, cancellationToken: TestContext.Current.CancellationToken);

        var activity = capture.Activities.Should()
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

        using var capture = ActivityCapture.Of(LedgerClient.ActivitySourceName);

        var client = CreateClientWithStateService();
        await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        var activity = capture.Activities.Should()
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

        using var capture = ActivityCapture.Of(LedgerClient.ActivitySourceName);

        var client = CreateClientWithStateService();

        var act = () => client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<RpcException>();

        var activity = capture.Activities.Should().ContainSingle(a => a.StatusDescription == detail).Subject;
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.GetTagItem(ActivityHelper.ErrorType).Should().Be(StatusCode.Unavailable.ToString());
    }

    [Fact]
    public async Task SubmitAndWaitAsync_tags_the_activity_with_grpc_semconv()
    {
        _commandService
            .SubmitAndWaitAsync(
                Arg.Any<SubmitAndWaitRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AsyncUnaryCall<SubmitAndWaitResponse>(
                Task.FromResult(new SubmitAndWaitResponse { UpdateId = "update-1", CompletionOffset = 1L }),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));

        using var capture = ActivityCapture.Of(LedgerClient.ActivitySourceName);

        var submission = RuntimeCommands.CommandsSubmission.Single(ArchiveCommand(UniqueContractId()))
            .WithActAs(ActAs);

        var client = CreateClient();
        await client.SubmitAndWaitAsync(submission, cancellationToken: TestContext.Current.CancellationToken);

        var activity = capture.Activities.Should()
            .ContainSingle(a => a.GetTagItem(ActivityHelper.RpcMethod) as string == "SubmitAndWait")
            .Subject;
        activity.Kind.Should().Be(ActivityKind.Client);
        activity.GetTagItem(ActivityHelper.RpcSystem).Should().Be("grpc");
        activity.GetTagItem(ActivityHelper.RpcService).Should().Be("com.daml.ledger.api.v2.CommandService");
        activity.GetTagItem(ActivityHelper.ServerAddress).Should().Be("localhost");
        activity.GetTagItem(ActivityHelper.ServerPort).Should().Be(5001);
    }

    [Fact]
    public async Task SubmitAndWaitAsync_records_an_RpcException_as_an_activity_error()
    {
        var detail = $"network down {Guid.NewGuid()}";
        var ex = new RpcException(new Status(StatusCode.Unavailable, detail));
        _commandService
            .SubmitAndWaitAsync(
                Arg.Any<SubmitAndWaitRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AsyncUnaryCall<SubmitAndWaitResponse>(
                Task.FromException<SubmitAndWaitResponse>(ex),
                Task.FromResult(new Metadata()),
                () => ex.Status,
                () => new Metadata(),
                () => { }));

        using var capture = ActivityCapture.Of(LedgerClient.ActivitySourceName);

        var submission = RuntimeCommands.CommandsSubmission.Single(ArchiveCommand(UniqueContractId()))
            .WithActAs(ActAs);

        var client = CreateClient();

        var act = () => client.SubmitAndWaitAsync(submission, cancellationToken: TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<RpcException>();

        var activity = capture.Activities.Should().ContainSingle(a => a.StatusDescription == detail).Subject;
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.GetTagItem(ActivityHelper.ErrorType).Should().Be(StatusCode.Unavailable.ToString());
        activity.GetTagItem(ActivityHelper.RpcGrpcStatusCode).Should().Be((int)StatusCode.Unavailable);
    }

    [Fact]
    public async Task EstimateTrafficCostAsync_tags_the_activity_with_grpc_semconv_and_the_total_traffic_cost()
    {
        var totalCost = Random.Shared.NextInt64(1_000_000, 2_000_000);
        _interactiveSubmissionService
            .PrepareSubmissionAsync(
                Arg.Any<Interactive.PrepareSubmissionRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AsyncUnaryCall<Interactive.PrepareSubmissionResponse>(
                Task.FromResult(new Interactive.PrepareSubmissionResponse
                {
                    CostEstimation = new Interactive.CostEstimation
                    {
                        TotalTrafficCostEstimation = (ulong)totalCost,
                    },
                }),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));

        using var capture = ActivityCapture.Of(LedgerClient.ActivitySourceName);

        var client = CreateClientWithInteractiveSubmissionService();
        await client.EstimateTrafficCostAsync(
            RuntimeCommands.CommandsSubmission.Single(
                RuntimeCommands.CreateCommand.For(new FooBar("alice")), ActAs),
            cancellationToken: TestContext.Current.CancellationToken);

        var activity = capture.Activities.Should()
            .ContainSingle(a => a.GetTagItem(LedgerClientActivityTags.CantonTrafficCostBytes) as long? == totalCost)
            .Subject;
        activity.Kind.Should().Be(ActivityKind.Client);
        activity.GetTagItem(ActivityHelper.RpcSystem).Should().Be("grpc");
        activity.GetTagItem(ActivityHelper.RpcService).Should()
            .Be("com.daml.ledger.api.v2.interactive.InteractiveSubmissionService");
        activity.GetTagItem(ActivityHelper.RpcMethod).Should().Be("PrepareSubmission");
    }
}
