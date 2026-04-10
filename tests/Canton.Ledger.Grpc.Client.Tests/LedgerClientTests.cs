// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Canton.Ledger.Auth;
using Com.Daml.Ledger.Api.V2;
using Daml.Codegen.CSharp.Runtime.Contracts;
using Daml.Codegen.CSharp.Runtime.Data;
using FluentAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using Xunit;
using RuntimeCommands = Daml.Codegen.CSharp.Runtime.Commands;
using RuntimeIdentifier = Daml.Codegen.CSharp.Runtime.Data.Identifier;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using ProtoRecord = Com.Daml.Ledger.Api.V2.Record;
using ProtoValue = Com.Daml.Ledger.Api.V2.Value;

namespace Canton.Ledger.Grpc.Client.Tests;

public class LedgerClientTests
{
    private readonly LedgerClientOptions _options;
    private readonly GrpcChannel _channel;
    private readonly CommandService.CommandServiceClient _commandService;
    private readonly ITokenProvider _tokenProvider = new StaticTokenProvider("test-token");

    public LedgerClientTests()
    {
        _options = new LedgerClientOptions
        {
            GrpcAddress = "https://localhost:5001",
            UserId = "test-user"
        };

        // Create a real channel (won't be used since we mock service client)
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);

        // Create a mock CallInvoker and use ForPartsOf to create a partial mock of the service client
        var callInvoker = Substitute.For<CallInvoker>();
        _commandService = Substitute.ForPartsOf<CommandService.CommandServiceClient>(callInvoker);
    }

    private LedgerClient CreateClient() => new(_options, _channel, _commandService, _tokenProvider);

    // ──────────────────────────────────────────────────────────────
    // BuildCommands
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void build_commands_sets_command_id_and_workflow_id()
    {
        var createCommand = new RuntimeCommands.CreateCommand(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            new DamlRecord(null, []));

        var submission = RuntimeCommands.CommandsSubmission.Single(createCommand)
            .WithActAs((Party)"party::alice")
            .WithCommandId("cmd-123")
            .WithWorkflowId("workflow-456");

        var client = CreateClient();
        var commands = client.BuildCommands(submission);

        commands.CommandId.Should().Be("cmd-123");
        commands.WorkflowId.Should().Be("workflow-456");
        commands.UserId.Should().Be("test-user");
        commands.ActAs.Should().ContainSingle().Which.Should().Be("party::alice");
    }

    [Fact]
    public void build_commands_generates_command_id_when_not_provided()
    {
        var createCommand = new RuntimeCommands.CreateCommand(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            new DamlRecord(null, []));

        var submission = RuntimeCommands.CommandsSubmission.Single(createCommand)
            .WithActAs((Party)"party::alice");

        var client = CreateClient();
        var commands = client.BuildCommands(submission);

        commands.CommandId.Should().NotBeNullOrEmpty();
        Guid.TryParse(commands.CommandId, out _).Should().BeTrue();
    }

    [Fact]
    public void build_commands_adds_create_command()
    {
        var createCommand = new RuntimeCommands.CreateCommand(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            new DamlRecord(
                new RuntimeIdentifier("pkg", "Module", "Template"),
                [new DamlField("owner", new DamlParty("party::alice"))]));

        var submission = RuntimeCommands.CommandsSubmission.Single(createCommand)
            .WithActAs((Party)"party::alice")
            .WithCommandId("test-cmd");

        var client = CreateClient();
        var commands = client.BuildCommands(submission);

        commands.Commands_.Should().ContainSingle();
        commands.Commands_[0].Create.Should().NotBeNull();
        commands.Commands_[0].Create.TemplateId.ModuleName.Should().Be("Module");
        commands.Commands_[0].Create.TemplateId.EntityName.Should().Be("Template");
    }

    [Fact]
    public void build_commands_adds_exercise_command()
    {
        var exerciseCommand = new RuntimeCommands.ExerciseCommand(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            "00contract123",
            "Archive",
            DamlUnit.Instance);

        var submission = RuntimeCommands.CommandsSubmission.Single(exerciseCommand)
            .WithActAs((Party)"party::alice")
            .WithCommandId("test-cmd");

        var client = CreateClient();
        var commands = client.BuildCommands(submission);

        commands.Commands_.Should().ContainSingle();
        commands.Commands_[0].Exercise.Should().NotBeNull();
        commands.Commands_[0].Exercise.ContractId.Should().Be("00contract123");
        commands.Commands_[0].Exercise.Choice.Should().Be("Archive");
    }

    [Fact]
    public void build_commands_includes_read_as_parties()
    {
        var createCommand = new RuntimeCommands.CreateCommand(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            new DamlRecord(null, []));

        var submission = RuntimeCommands.CommandsSubmission.Single(createCommand)
            .WithActAs((Party)"party::alice")
            .WithReadAs((Party)"party::observer1", (Party)"party::observer2")
            .WithCommandId("test-cmd");

        var client = CreateClient();
        var commands = client.BuildCommands(submission);

        commands.ReadAs.Should().HaveCount(2);
        commands.ReadAs.Should().Contain("party::observer1");
        commands.ReadAs.Should().Contain("party::observer2");
    }

    // ──────────────────────────────────────────────────────────────
    // SubmitAsync / SubmitAndWaitForTransactionAsync
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task submit_async_returns_update_id()
    {
        var response = new SubmitAndWaitResponse { UpdateId = "update-123" };

        _commandService
            .SubmitAndWaitAsync(
                Arg.Any<SubmitAndWaitRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AsyncUnaryCall<SubmitAndWaitResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));

        var createCommand = new RuntimeCommands.CreateCommand(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            new DamlRecord(null, []));

        var submission = RuntimeCommands.CommandsSubmission.Single(createCommand)
            .WithActAs((Party)"party::alice")
            .WithCommandId("test-cmd");

        var client = CreateClient();
        var result = await client.SubmitAsync(submission);

        result.Should().Be("update-123");
    }

    [Fact]
    public async Task submit_and_wait_for_transaction_returns_created_contracts()
    {
        var transaction = new Transaction
        {
            UpdateId = "update-123",
            Offset = 456L
        };
        transaction.Events.Add(new Event
        {
            Created = new Com.Daml.Ledger.Api.V2.CreatedEvent
            {
                ContractId = "00contract789",
                TemplateId = new ProtoIdentifier
                {
                    PackageId = "pkg",
                    ModuleName = "Module",
                    EntityName = "Template"
                },
                CreateArguments = new ProtoRecord()
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

        var createCommand = new RuntimeCommands.CreateCommand(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            new DamlRecord(null, []));

        var submission = RuntimeCommands.CommandsSubmission.Single(createCommand)
            .WithActAs((Party)"party::alice")
            .WithCommandId("test-cmd");

        var client = CreateClient();
        var result = await client.SubmitAndWaitForTransactionAsync(submission);

        result.UpdateId.Should().Be("update-123");
        result.CompletionOffset.Should().Be(456L);
        result.CreatedContracts.Should().ContainSingle();
        result.CreatedContracts[0].ContractId.Should().Be("00contract789");
    }

    [Fact]
    public async Task submit_and_wait_for_transaction_returns_archived_contracts()
    {
        var transaction = new Transaction
        {
            UpdateId = "update-123",
            Offset = 456L
        };
        transaction.Events.Add(new Event
        {
            Archived = new Com.Daml.Ledger.Api.V2.ArchivedEvent
            {
                ContractId = "00archived123"
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

        var exerciseCommand = new RuntimeCommands.ExerciseCommand(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            "00archived123",
            "Archive",
            DamlUnit.Instance);

        var submission = RuntimeCommands.CommandsSubmission.Single(exerciseCommand)
            .WithActAs((Party)"party::alice")
            .WithCommandId("test-cmd");

        var client = CreateClient();
        var result = await client.SubmitAndWaitForTransactionAsync(submission);

        result.ArchivedContractIds.Should().ContainSingle().Which.Should().Be("00archived123");
    }

    [Fact]
    public async Task throws_when_token_provider_returns_empty_token()
    {
        var emptyProvider = Substitute.For<ITokenProvider>();
        emptyProvider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns("");

        var client = new LedgerClient(_options, _channel, _commandService, emptyProvider);

        var submission = RuntimeCommands.CommandsSubmission.Single(
                new RuntimeCommands.CreateCommand(
                    new RuntimeIdentifier("pkg", "Module", "Template"),
                    new DamlRecord(null, [])))
            .WithActAs((Party)"party::alice")
            .WithCommandId("test-cmd");

        var act = () => client.SubmitAndWaitForTransactionAsync(submission);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*returned an empty token*");
    }

    [Fact]
    public async Task throws_when_token_provider_returns_whitespace_token()
    {
        var whitespaceProvider = Substitute.For<ITokenProvider>();
        whitespaceProvider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns("   ");

        var client = new LedgerClient(_options, _channel, _commandService, whitespaceProvider);

        var submission = RuntimeCommands.CommandsSubmission.Single(
                new RuntimeCommands.CreateCommand(
                    new RuntimeIdentifier("pkg", "Module", "Template"),
                    new DamlRecord(null, [])))
            .WithActAs((Party)"party::alice")
            .WithCommandId("test-cmd");

        var act = () => client.SubmitAndWaitForTransactionAsync(submission);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*returned an empty token*");
    }

    [Fact]
    public void dispose_does_not_throw()
    {
        var client = CreateClient();

        var action = () => client.Dispose();

        action.Should().NotThrow();
    }

    // ──────────────────────────────────────────────────────────────
    // ExerciseAsync integration tests
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task exercise_async_throws_when_no_matching_event()
    {
        var transaction = new Transaction { UpdateId = "update-456", Offset = 789L };
        // No ExercisedEvent added — response has no matching event

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

        var exerciseCommand = new RuntimeCommands.ExerciseCommand(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            "00contract123",
            "Archive",
            DamlUnit.Instance);

        var client = CreateClient();

        var action = () => client.ExerciseAsync<object>(exerciseCommand, "party::alice");

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No ExercisedEvent found*Archive*00contract123*");
    }

    [Fact]
    public async Task exercise_async_returns_contract_id_result()
    {
        var transaction = new Transaction { UpdateId = "update-456", Offset = 789L };
        transaction.Events.Add(new Event
        {
            Exercised = new ExercisedEvent
            {
                ContractId = "00contract123",
                TemplateId = new ProtoIdentifier { PackageId = "pkg", ModuleName = "Module", EntityName = "Template" },
                Choice = "Accept",
                ExerciseResult = new ProtoValue { ContractId = "00newcontract456" }
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

        var exerciseCommand = new RuntimeCommands.ExerciseCommand(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            "00contract123",
            "Accept",
            DamlUnit.Instance);

        var client = CreateClient();
        var result = await client.ExerciseAsync<ContractId<TestTemplate>>(
            exerciseCommand, "party::alice");

        result.Value.Should().Be("00newcontract456");
    }

    [Fact]
    public async Task exercise_async_returns_unit_for_void_choice()
    {
        var transaction = new Transaction { UpdateId = "update-456", Offset = 789L };
        transaction.Events.Add(new Event
        {
            Exercised = new ExercisedEvent
            {
                ContractId = "00contract123",
                TemplateId = new ProtoIdentifier { PackageId = "pkg", ModuleName = "Module", EntityName = "Template" },
                Choice = "Archive",
                ExerciseResult = new ProtoValue { Unit = new Google.Protobuf.WellKnownTypes.Empty() }
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

        var exerciseCommand = new RuntimeCommands.ExerciseCommand(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            "00contract123",
            "Archive",
            DamlUnit.Instance);

        var client = CreateClient();

        // void overload should not throw
        await client.ExerciseAsync(exerciseCommand, "party::alice");
    }

    [Fact]
    public async Task exercise_async_uses_ledger_effects_shape()
    {
        SubmitAndWaitForTransactionRequest? capturedRequest = null;

        var transaction = new Transaction { UpdateId = "update-456", Offset = 789L };
        transaction.Events.Add(new Event
        {
            Exercised = new ExercisedEvent
            {
                ContractId = "00contract123",
                TemplateId = new ProtoIdentifier { PackageId = "pkg", ModuleName = "Module", EntityName = "Template" },
                Choice = "Archive",
                ExerciseResult = new ProtoValue { Unit = new Google.Protobuf.WellKnownTypes.Empty() }
            }
        });

        var response = new SubmitAndWaitForTransactionResponse { Transaction = transaction };

        _commandService
            .SubmitAndWaitForTransactionAsync(
                Arg.Do<SubmitAndWaitForTransactionRequest>(r => capturedRequest = r),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AsyncUnaryCall<SubmitAndWaitForTransactionResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));

        var exerciseCommand = new RuntimeCommands.ExerciseCommand(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            "00contract123",
            "Archive",
            DamlUnit.Instance);

        var client = CreateClient();
        await client.ExerciseAsync(exerciseCommand, "party::alice");

        capturedRequest.Should().NotBeNull();
        capturedRequest!.TransactionFormat.Should().NotBeNull();
        capturedRequest.TransactionFormat.TransactionShape.Should().Be(TransactionShape.LedgerEffects);
        capturedRequest.TransactionFormat.EventFormat.Should().NotBeNull();
        capturedRequest.TransactionFormat.EventFormat.Verbose.Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────
    // Test template — minimal ITemplate for unit tests
    // ──────────────────────────────────────────────────────────────

    internal sealed record TestTemplate(string Owner) : ITemplate
    {
        public static RuntimeIdentifier TemplateId { get; } = new("pkg", "Module", "Template");
        public static string PackageId => "pkg";
        public static string PackageName => "test-package";
        public static Version PackageVersion { get; } = new(0, 1, 0);

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("owner", new DamlParty(Owner)));
    }
}
