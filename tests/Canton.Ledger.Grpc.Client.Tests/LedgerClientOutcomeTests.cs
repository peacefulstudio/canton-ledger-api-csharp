// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Canton.Ledger.Auth;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using FluentAssertions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using Xunit;
using GrpcStatus = Google.Rpc.Status;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using ProtoRecord = Com.Daml.Ledger.Api.V2.Record;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using Status = Grpc.Core.Status;

namespace Canton.Ledger.Grpc.Client.Tests;

public class LedgerClientOutcomeTests
{
    private readonly LedgerClientOptions _options;
    private readonly GrpcChannel _channel;
    private readonly CommandService.CommandServiceClient _commandService;
    private readonly ITokenProvider _tokenProvider = new StaticTokenProvider("test-token");

    public LedgerClientOutcomeTests()
    {
        _options = new LedgerClientOptions
        {
            GrpcAddress = "https://localhost:5001",
            UserId = "test-user",
        };
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);
        var callInvoker = Substitute.For<CallInvoker>();
        _commandService = Substitute.ForPartsOf<CommandService.CommandServiceClient>(callInvoker);
    }

    private LedgerClient CreateClient() => new(_options, _channel, _commandService, _tokenProvider);

    [Fact]
    public async Task TrySubmitAndWaitForTransaction_returns_Created_on_ok()
    {
        var transaction = new Transaction { UpdateId = "u-1", Offset = 1L };
        transaction.Events.Add(new Event
        {
            Created = new ProtoCreatedEvent
            {
                ContractId = "00abc",
                TemplateId = new ProtoIdentifier { PackageId = "test-pkg", ModuleName = "Murmures.Foo", EntityName = "FooBar" },
                CreateArguments = new ProtoRecord(),
            },
        });
        StubCommandService(new SubmitAndWaitForTransactionResponse { Transaction = transaction });

        var client = CreateClient();
        var outcome = await client.TrySubmitAndWaitForTransactionAsync(MakeFooBarCreate(), TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>();
        var success = (ExerciseOutcome<TransactionResult>.One)outcome;
        success.Result.UpdateId.Should().Be("u-1");
        success.Result.Single<FooBar>().Value.Should().Be("00abc");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransaction_returns_DamlError_on_structured_failure()
    {
        var ex = MakeDamlRpcException(
            "MURMURES_SWAP_ALREADY_EXECUTED",
            "swap already executed",
            "InvalidGivenCurrentSystemStateOther");
        StubCommandServiceFailure(ex);

        var client = CreateClient();
        var outcome = await client.TrySubmitAndWaitForTransactionAsync(MakeFooBarCreate(), TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.DamlError>();
        var err = (ExerciseOutcome<TransactionResult>.DamlError)outcome;
        err.Category.Should().Be(DamlErrorCategory.InvalidGivenCurrentSystemStateOther);
        err.ErrorId.Should().Be("MURMURES_SWAP_ALREADY_EXECUTED");
        err.Message.Should().Be("swap already executed");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransaction_returns_InfraError_on_unstructured_failure()
    {
        // RpcException without trailers → no rich error info → InfraError.
        var ex = new RpcException(new Status(StatusCode.Unavailable, "network down"));
        StubCommandServiceFailure(ex);

        var client = CreateClient();
        var outcome = await client.TrySubmitAndWaitForTransactionAsync(MakeFooBarCreate(), TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.InfraError>();
        var infra = (ExerciseOutcome<TransactionResult>.InfraError)outcome;
        infra.StatusCode.Should().Be((int)StatusCode.Unavailable);
        infra.Message.Should().Be("network down");
    }

    [Fact]
    public async Task TryCreateAsync_returns_Created_on_success()
    {
        var transaction = new Transaction { UpdateId = "u-1", Offset = 1L };
        transaction.Events.Add(new Event
        {
            Created = new ProtoCreatedEvent
            {
                ContractId = "00xyz",
                TemplateId = new ProtoIdentifier { PackageId = "test-pkg", ModuleName = "Murmures.Foo", EntityName = "FooBar" },
                CreateArguments = new ProtoRecord(),
            },
        });
        StubCommandService(new SubmitAndWaitForTransactionResponse { Transaction = transaction });

        var client = CreateClient();
        var outcome = await client.TryCreateAsync(new FooBar("alice"), "party::alice", cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<FooBar>>.One>();
        var created = (ExerciseOutcome<ContractId<FooBar>>.One)outcome;
        created.Result.Value.Should().Be("00xyz");
    }

    [Fact]
    public async Task TryCreateAsync_returns_None_when_no_matching_template()
    {
        // Server returns a transaction but no Created event (rare but representable).
        var transaction = new Transaction { UpdateId = "u-1", Offset = 1L };
        StubCommandService(new SubmitAndWaitForTransactionResponse { Transaction = transaction });

        var client = CreateClient();
        var outcome = await client.TryCreateAsync(new FooBar("alice"), "party::alice", cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<FooBar>>.None>();
    }

    [Fact]
    public async Task TryCreateAsync_returns_DamlError_on_structured_failure()
    {
        var ex = MakeDamlRpcException(
            "DUPLICATE_COMMAND",
            "duplicate command",
            "InvalidGivenCurrentSystemStateResourceExists");
        StubCommandServiceFailure(ex);

        var client = CreateClient();
        var outcome = await client.TryCreateAsync(new FooBar("alice"), "party::alice", cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<FooBar>>.DamlError>();
        var err = (ExerciseOutcome<ContractId<FooBar>>.DamlError)outcome;
        err.Category.Should().Be(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists);
        err.ErrorId.Should().Be("DUPLICATE_COMMAND");
    }

    [Fact]
    public async Task TryExerciseForCreatedAsync_returns_Many_when_multiple_matching_creates()
    {
        var transaction = new Transaction { UpdateId = "u-1", Offset = 1L };
        var tid = new ProtoIdentifier { PackageId = "test-pkg", ModuleName = "Murmures.Foo", EntityName = "FooBar" };
        transaction.Events.Add(new Event { Created = new ProtoCreatedEvent { ContractId = "00a", TemplateId = tid, CreateArguments = new ProtoRecord() } });
        transaction.Events.Add(new Event { Created = new ProtoCreatedEvent { ContractId = "00b", TemplateId = tid, CreateArguments = new ProtoRecord() } });
        StubCommandService(new SubmitAndWaitForTransactionResponse { Transaction = transaction });

        var exercise = new RuntimeCommands.ExerciseCommand(
            new RuntimeIdentifier("test-pkg", "Murmures.Foo", "FooBar"),
            "00contract",
            "Multiply",
            DamlUnit.Instance);

        var client = CreateClient();
        var outcome = await client.TryExerciseForCreatedAsync<FooBar>(exercise, "party::alice", cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<FooBar>>.Many>();
        var many = (ExerciseOutcome<ContractId<FooBar>>.Many)outcome;
        many.Count.Should().Be(2);
        many.ContractIds.Should().Equal("00a", "00b");
    }

    private void StubCommandService(SubmitAndWaitForTransactionResponse response)
    {
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
    }

    private void StubCommandServiceFailure(RpcException exception)
    {
        _commandService
            .SubmitAndWaitForTransactionAsync(
                Arg.Any<SubmitAndWaitForTransactionRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AsyncUnaryCall<SubmitAndWaitForTransactionResponse>(
                Task.FromException<SubmitAndWaitForTransactionResponse>(exception),
                Task.FromResult(new Metadata()),
                () => exception.Status,
                () => exception.Trailers ?? new Metadata(),
                () => { }));
    }

    private static RuntimeCommands.CommandsSubmission MakeFooBarCreate()
    {
        var create = new RuntimeCommands.CreateCommand(
            new RuntimeIdentifier("test-pkg", "Murmures.Foo", "FooBar"),
            new DamlRecord(null, []));
        return RuntimeCommands.CommandsSubmission.Single(create)
            .WithActAs((Party)"party::alice")
            .WithCommandId("test-cmd");
    }

    private static RpcException MakeDamlRpcException(string errorId, string message, string category)
    {
        var info = new ErrorInfo { Reason = errorId, Domain = "ledger.api" };
        info.Metadata.Add("category", category);
        var status = new GrpcStatus { Code = (int)StatusCode.FailedPrecondition, Message = message };
        status.Details.Add(Any.Pack(info));
        var trailers = new Metadata { { "grpc-status-details-bin", status.ToByteArray() } };
        return new RpcException(new Status(StatusCode.FailedPrecondition, message), trailers);
    }

    internal sealed record FooBar(string Owner) : ITemplate
    {
        public static RuntimeIdentifier TemplateId { get; } = new("test-pkg", "Murmures.Foo", "FooBar");
        public static string PackageId => "test-pkg";
        public static string PackageName => "test-package";
        public static Version PackageVersion { get; } = new(0, 1, 0);

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("owner", new DamlParty(Owner)));
    }
}
