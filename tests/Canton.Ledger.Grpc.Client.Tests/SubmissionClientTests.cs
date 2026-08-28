// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Kernel.Resilience;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using AwesomeAssertions;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using ProtoArchivedEvent = Com.Daml.Ledger.Api.V2.ArchivedEvent;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoExercisedEvent = Com.Daml.Ledger.Api.V2.ExercisedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using ProtoRecord = Com.Daml.Ledger.Api.V2.Record;
using Status = Grpc.Core.Status;

namespace Canton.Ledger.Grpc.Client.Tests;

public class SubmissionClientTests
{
    private static readonly Party ActAs = new("party::alice");
    private static readonly RuntimeCommands.CommandId TestCommandId = new("test-cmd");
    private static readonly ProtoIdentifier TestTemplateId = new()
    {
        PackageId = "pkg",
        ModuleName = "Module",
        EntityName = "Template",
    };

    private readonly LedgerClientOptions _options;
    private readonly CommandService.CommandServiceClient _commandService;
    private readonly CommandSubmissionService.CommandSubmissionServiceClient _commandSubmissionService;
    private readonly ITokenProvider _tokenProvider = new StaticTokenProvider("test-token");

    public SubmissionClientTests()
    {
        _options = new LedgerClientOptions
        {
            GrpcAddress = "https://localhost:5001",
            UserId = "test-user",
        };

        var callInvoker = Substitute.For<CallInvoker>();
        _commandService = Substitute.ForPartsOf<CommandService.CommandServiceClient>(callInvoker);
        _commandSubmissionService = Substitute.ForPartsOf<CommandSubmissionService.CommandSubmissionServiceClient>(callInvoker);
    }

    private SubmissionClient CreateClient(
        Func<long, RuntimeCommands.SubmitterInfo, CancellationToken, Task<TransactionResult>>? pointReadByOffset = null,
        Func<long, RuntimeCommands.SubmitterInfo, CancellationToken, Task<TransactionTree>>? treePointReadByOffset = null,
        ILogger? logger = null) =>
        new(
            new LedgerCallInvoker(_options, _tokenProvider),
            _commandService,
            _commandSubmissionService,
            new CommandBuilder(_options),
            _options,
            logger ?? NullLogger<LedgerClient>.Instance,
            pointReadByOffset ?? ((_, _, _) => throw new InvalidOperationException("point read not expected")),
            treePointReadByOffset ?? ((_, _, _) => throw new InvalidOperationException("tree point read not expected")));

    private static RuntimeCommands.CommandsSubmission Create() =>
        RuntimeCommands.CommandsSubmission
            .Single(new RuntimeCommands.CreateCommand(
                new RuntimeIdentifier("pkg", "Module", "Template"),
                new DamlRecord(null, [])))
            .WithActAs(ActAs)
            .WithCommandId(TestCommandId);

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_resolves_a_retried_duplicate_through_the_injected_point_read()
    {
        _options.Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 2, Delay = TimeSpan.Zero };
        StubSubmitAndWaitForTransaction(
            Faulted<SubmitAndWaitForTransactionResponse>(Unavailable()),
            Faulted<SubmitAndWaitForTransactionResponse>(DuplicateCommand(completionOffset: 42L)));

        var resolved = GrpcTransactionResultProjector.Project(
            new SubmitAndWaitForTransactionResponse
            {
                Transaction = new Transaction { UpdateId = "u-original", Offset = 42L },
            });
        long? readOffset = null;
        var client = CreateClient((offset, _, _) =>
        {
            readOffset = offset;
            return Task.FromResult(resolved);
        });

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(Create(), cancellationToken: TestContext.Current.CancellationToken);

        var success = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>(
            "a duplicate on a retried attempt proves the first attempt committed").Subject;
        success.Result.UpdateId.Should().Be("u-original");
        readOffset.Should().Be(42L,
            "the completion_offset carried on the duplicate rejection is resolved via the injected point read");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_keeps_a_first_attempt_duplicate_without_touching_the_point_read()
    {
        _options.Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 2, Delay = TimeSpan.Zero };
        StubSubmitAndWaitForTransaction(Faulted<SubmitAndWaitForTransactionResponse>(DuplicateCommand(completionOffset: 42L)));
        var pointReadInvoked = false;
        var client = CreateClient((_, _, _) =>
        {
            pointReadInvoked = true;
            return Task.FromException<TransactionResult>(new InvalidOperationException("unexpected"));
        });

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(Create(), cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.DamlError>()
            .Which.ErrorId.Should().Be("DUPLICATE_COMMAND");
        pointReadInvoked.Should().BeFalse("a first-attempt duplicate is a genuine caller error, not a lost success");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionTreeAsync_resolves_a_retried_duplicate_through_the_injected_tree_point_read()
    {
        _options.Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 2, Delay = TimeSpan.Zero };
        StubSubmitAndWaitForTransaction(
            Faulted<SubmitAndWaitForTransactionResponse>(Unavailable()),
            Faulted<SubmitAndWaitForTransactionResponse>(DuplicateCommand(completionOffset: 42L)));

        var resolved = GrpcTransactionTreeProjector.Project(
            new Transaction { UpdateId = "u-original", Offset = 42L });
        long? readOffset = null;
        var client = CreateClient(treePointReadByOffset: (offset, _, _) =>
        {
            readOffset = offset;
            return Task.FromResult(resolved);
        });

        var outcome = await client.TrySubmitAndWaitForTransactionTreeAsync(
            Create(), Submitter(), cancellationToken: TestContext.Current.CancellationToken);

        var success = outcome.Should().BeOfType<ExerciseOutcome<TransactionTree>.One>(
            "a duplicate on a retried attempt proves the first attempt committed").Subject;
        success.Result.UpdateId.Should().Be("u-original");
        readOffset.Should().Be(42L,
            "the tree path resolves the completion_offset through the tree point read, not the flat one");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionTreeAsync_counts_a_consuming_exercise_as_an_archive()
    {
        var loggerFactory = new CapturingLoggerFactory();
        StubSubmitAndWaitForTransaction(Completed(LedgerEffectsTransaction()));

        var client = CreateClient(logger: new Logger<LedgerClient>(loggerFactory));
        await client.TrySubmitAndWaitForTransactionTreeAsync(
            Create(), Submitter(), cancellationToken: TestContext.Current.CancellationToken);

        loggerFactory.Records.Should().Contain(
            r => r.Message.Contains("Created: 1, Archived: 1", StringComparison.Ordinal),
            "a ledger-effects transaction reports consumption as a consuming exercise, never as an ArchivedEvent");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_counts_an_archived_event_as_an_archive()
    {
        var loggerFactory = new CapturingLoggerFactory();
        StubSubmitAndWaitForTransaction(Completed(AcsDeltaTransaction()));

        var client = CreateClient(logger: new Logger<LedgerClient>(loggerFactory));
        await client.TrySubmitAndWaitForTransactionAsync(
            Create(), cancellationToken: TestContext.Current.CancellationToken);

        loggerFactory.Records.Should().Contain(
            r => r.Message.Contains("Created: 1, Archived: 1", StringComparison.Ordinal));
    }

    private static RuntimeCommands.SubmitterInfo Submitter() => new(new HashSet<Party> { ActAs });

    private static Transaction LedgerEffectsTransaction()
    {
        var transaction = new Transaction { UpdateId = "u-effects", Offset = 42L };
        transaction.Events.Add(Exercised(nodeId: 0, lastDescendantNodeId: 1, "Consume", consuming: true));
        transaction.Events.Add(Created(nodeId: 1, "00created"));
        transaction.Events.Add(Exercised(nodeId: 2, lastDescendantNodeId: 2, "Peek", consuming: false));
        return transaction;
    }

    private static Transaction AcsDeltaTransaction()
    {
        var transaction = new Transaction { UpdateId = "u-delta", Offset = 42L };
        transaction.Events.Add(Created(nodeId: 0, "00created"));
        transaction.Events.Add(new Event
        {
            Archived = new ProtoArchivedEvent
            {
                NodeId = 1,
                ContractId = "00archived",
                TemplateId = TestTemplateId,
            },
        });
        return transaction;
    }

    private static Event Created(int nodeId, string contractId) => new()
    {
        Created = new ProtoCreatedEvent
        {
            NodeId = nodeId,
            ContractId = contractId,
            TemplateId = TestTemplateId,
            CreateArguments = new ProtoRecord(),
        },
    };

    private static Event Exercised(int nodeId, int lastDescendantNodeId, string choice, bool consuming) => new()
    {
        Exercised = new ProtoExercisedEvent
        {
            NodeId = nodeId,
            LastDescendantNodeId = lastDescendantNodeId,
            ContractId = "00target",
            TemplateId = TestTemplateId,
            Choice = choice,
            Consuming = consuming,
        },
    };

    private static AsyncUnaryCall<SubmitAndWaitForTransactionResponse> Completed(Transaction transaction) =>
        new(
            Task.FromResult(new SubmitAndWaitForTransactionResponse { Transaction = transaction }),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

    private void StubSubmitAndWaitForTransaction(params AsyncUnaryCall<SubmitAndWaitForTransactionResponse>[] calls) =>
        _commandService
            .SubmitAndWaitForTransactionAsync(
                Arg.Any<SubmitAndWaitForTransactionRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(calls[0], calls[1..]);

    private static RpcException Unavailable() => new(new Status(StatusCode.Unavailable, "transient down"));

    private static RpcException DuplicateCommand(long? completionOffset) =>
        LedgerClientTestFixtures.MakeDamlRpcException(
            "DUPLICATE_COMMAND",
            "duplicate",
            "InvalidGivenCurrentSystemStateResourceExists",
            StatusCode.AlreadyExists,
            completionOffset is { } offset
                ? new Dictionary<string, string> { ["completion_offset"] = offset.ToString(CultureInfo.InvariantCulture) }
                : null);

    private static AsyncUnaryCall<T> Faulted<T>(RpcException exception) =>
        new(
            Task.FromException<T>(exception),
            Task.FromResult(new Metadata()),
            () => exception.Status,
            () => exception.Trailers ?? new Metadata(),
            () => { });
}
