// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using System.Diagnostics;
using Canton.Ledger.Auth;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using ProtoExercisedEvent = Com.Daml.Ledger.Api.V2.ExercisedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using ProtoRecord = Com.Daml.Ledger.Api.V2.Record;
using ProtoValue = Com.Daml.Ledger.Api.V2.Value;
using Status = Grpc.Core.Status;

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
    [Fact]
    public void BuildCommands_sets_command_id_and_workflow_id()
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
    public void BuildCommands_generates_command_id_when_not_provided()
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
    public void BuildCommands_adds_create_command()
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
    public void BuildCommands_adds_exercise_command()
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
    public void BuildCommands_includes_read_as_parties()
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

    [Fact]
    public async Task SubmitAsync_returns_update_id()
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
        var result = await client.SubmitAsync(submission, TestContext.Current.CancellationToken);

        result.Should().Be("update-123");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransaction_projects_created_contracts()
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
        var outcome = await client.TrySubmitAndWaitForTransactionAsync(submission, TestContext.Current.CancellationToken);

        var success = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>().Subject;
        success.Result.UpdateId.Should().Be("update-123");
        success.Result.CompletionOffset.Should().Be(456L);
        success.Result.CreatedContracts.Should().ContainSingle();
        success.Result.CreatedContracts[0].ContractId.Should().Be("00contract789");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransaction_projects_archived_contracts()
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
        var outcome = await client.TrySubmitAndWaitForTransactionAsync(submission, TestContext.Current.CancellationToken);

        var success = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>().Subject;
        success.Result.ArchivedContractIds.Should().ContainSingle().Which.Should().Be("00archived123");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransaction_projects_exercised_events()
    {
        var transaction = new Transaction { UpdateId = "update-789", Offset = 999L };
        var templateId = new ProtoIdentifier { PackageId = "pkg", ModuleName = "Module", EntityName = "Template" };
        transaction.Events.Add(new Event
        {
            Exercised = new ProtoExercisedEvent
            {
                ContractId = "00contract999",
                TemplateId = templateId,
                Choice = "Transfer",
                ChoiceArgument = new ProtoValue { Unit = new Google.Protobuf.WellKnownTypes.Empty() },
                ExerciseResult = new ProtoValue { ContractId = "00new999" },
                Consuming = true,
                ActingParties = { "party::alice" },
                WitnessParties = { "party::alice", "party::bob" },
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
            "00contract999",
            "Transfer",
            DamlUnit.Instance);

        var submission = RuntimeCommands.CommandsSubmission.Single(exerciseCommand)
            .WithActAs((Party)"party::alice")
            .WithCommandId("test-cmd");

        var client = CreateClient();
        var outcome = await client.TrySubmitAndWaitForTransactionAsync(submission, TestContext.Current.CancellationToken);

        var success = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>().Subject;
        var ev = success.Result.ExercisedEvents.Should().ContainSingle().Subject;
        ev.ContractId.Should().Be("00contract999");
        ev.ChoiceName.Should().Be("Transfer");
        ev.Consuming.Should().BeTrue();
        ev.InterfaceId.Should().BeNull();
        ev.ActingParties.Should().BeEquivalentTo([(Party)"party::alice"]);
        ev.WitnessParties.Should().BeEquivalentTo([(Party)"party::alice", (Party)"party::bob"]);
        ev.TemplateId.ModuleName.Should().Be("Module");
        ev.TemplateId.EntityName.Should().Be("Template");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransaction_throws_when_token_provider_returns_empty_token()
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

        var act = () => client.TrySubmitAndWaitForTransactionAsync(submission, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*returned an empty token*");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransaction_throws_when_token_provider_returns_whitespace_token()
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

        var act = () => client.TrySubmitAndWaitForTransactionAsync(submission, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*returned an empty token*");
    }

    [Fact]
    public void Dispose_does_not_throw()
    {
        var client = CreateClient();

        var action = () => client.Dispose();

        action.Should().NotThrow();
    }

    [Fact]
    public async Task Dispose_does_not_disable_tracing_for_subsequent_instances()
    {
        var response = new SubmitAndWaitForTransactionResponse
        {
            Transaction = new Transaction { UpdateId = "update-1", Offset = 1L }
        };

        var secondCommandService = Substitute.ForPartsOf<CommandService.CommandServiceClient>(
            Substitute.For<CallInvoker>());
        secondCommandService
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

        var startedActivities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == LedgerClient.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = startedActivities.Add
        };
        ActivitySource.AddActivityListener(listener);

        using var firstChannel = GrpcChannel.ForAddress(_options.GrpcAddress);
        var firstClient = new LedgerClient(_options, firstChannel, _commandService, _tokenProvider);
        firstClient.Dispose();

        using var secondChannel = GrpcChannel.ForAddress(_options.GrpcAddress);
        var secondClient = new LedgerClient(_options, secondChannel, secondCommandService, _tokenProvider);
        var submission = RuntimeCommands.CommandsSubmission.Single(
                new RuntimeCommands.CreateCommand(
                    new RuntimeIdentifier("pkg", "Module", "Template"),
                    new DamlRecord(null, [])))
            .WithActAs((Party)"party::alice")
            .WithCommandId("test-cmd");

        await secondClient.TrySubmitAndWaitForTransactionAsync(submission, TestContext.Current.CancellationToken);

        startedActivities.Should().NotBeEmpty(
            "disposing one LedgerClient must not disable tracing for subsequent instances");
    }

    [Fact]
    public async Task TryExerciseAsync_throws_when_no_matching_event()
    {
        var transaction = new Transaction { UpdateId = "update-456", Offset = 789L };

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

        var action = () => client.TryExerciseAsync<object>(exerciseCommand, "party::alice", cancellationToken: TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no exercised event for choice*Archive*");
    }

    [Fact]
    public async Task TryExerciseAsync_throws_when_response_has_no_Transaction()
    {
        var response = new SubmitAndWaitForTransactionResponse();

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

        var action = () => client.TryExerciseAsync<object>(exerciseCommand, "party::alice", cancellationToken: TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no Transaction*");
    }

    [Fact]
    public async Task TryExerciseAsync_returns_One_when_ExercisedEvent_has_no_ExerciseResult()
    {
        var transaction = new Transaction { UpdateId = "update-456", Offset = 789L };
        transaction.Events.Add(new Event
        {
            Exercised = new ProtoExercisedEvent
            {
                ContractId = "00contract123",
                TemplateId = new ProtoIdentifier { PackageId = "pkg", ModuleName = "Module", EntityName = "Template" },
                Choice = "Archive",
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

        var outcome = await client.TryExerciseAsync<object>(
            exerciseCommand, "party::alice", cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<object>.One>();
    }

    [Fact]
    public async Task TryExerciseAsync_returns_One_with_contract_id()
    {
        var transaction = new Transaction { UpdateId = "update-456", Offset = 789L };
        transaction.Events.Add(new Event
        {
            Exercised = new ProtoExercisedEvent
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
        var outcome = await client.TryExerciseAsync<ContractId<TestTemplate>>(
            exerciseCommand, "party::alice", cancellationToken: TestContext.Current.CancellationToken);

        var success = outcome.Should().BeOfType<ExerciseOutcome<ContractId<TestTemplate>>.One>().Subject;
        success.Result.Value.Should().Be("00newcontract456");
    }

    [Fact]
    public async Task TryExerciseAsync_returns_One_for_void_choice()
    {
        var transaction = new Transaction { UpdateId = "update-456", Offset = 789L };
        transaction.Events.Add(new Event
        {
            Exercised = new ProtoExercisedEvent
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
        var outcome = await client.TryExerciseAsync<object>(
            exerciseCommand, "party::alice", cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<object>.One>();
    }

    [Fact]
    public async Task TryExerciseAsync_uses_ledger_effects_shape()
    {
        SubmitAndWaitForTransactionRequest? capturedRequest = null;

        var transaction = new Transaction { UpdateId = "update-456", Offset = 789L };
        transaction.Events.Add(new Event
        {
            Exercised = new ProtoExercisedEvent
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
        await client.TryExerciseAsync<object>(exerciseCommand, "party::alice", cancellationToken: TestContext.Current.CancellationToken);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.TransactionFormat.Should().NotBeNull();
        capturedRequest.TransactionFormat.TransactionShape.Should().Be(TransactionShape.LedgerEffects);
        capturedRequest.TransactionFormat.EventFormat.Should().NotBeNull();
        capturedRequest.TransactionFormat.EventFormat.Verbose.Should().BeTrue();
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransaction_uses_default_acs_delta_shape()
    {
        SubmitAndWaitForTransactionRequest? capturedRequest = null;

        var transaction = new Transaction { UpdateId = "update-456", Offset = 789L };
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

        var createCommand = new RuntimeCommands.CreateCommand(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            new DamlRecord(null, []));
        var submission = RuntimeCommands.CommandsSubmission.Single(createCommand)
            .WithActAs((Party)"party::alice")
            .WithCommandId("test-cmd");

        var client = CreateClient();
        await client.TrySubmitAndWaitForTransactionAsync(submission, TestContext.Current.CancellationToken);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.TransactionFormat.Should().BeNull(
            "the plain submit path must keep the server-default AcsDelta shape");
    }

    [Fact]
    public async Task TryExerciseAsync_throws_when_multiple_matching_events()
    {
        var transaction = new Transaction { UpdateId = "update-456", Offset = 789L };
        transaction.Events.Add(new Event
        {
            Exercised = new ProtoExercisedEvent
            {
                ContractId = "00contract123",
                TemplateId = new ProtoIdentifier { PackageId = "pkg", ModuleName = "Module", EntityName = "Template" },
                Choice = "Bump",
                ExerciseResult = new ProtoValue { Unit = new Google.Protobuf.WellKnownTypes.Empty() }
            }
        });
        transaction.Events.Add(new Event
        {
            Exercised = new ProtoExercisedEvent
            {
                ContractId = "00childcontract456",
                TemplateId = new ProtoIdentifier { PackageId = "pkg", ModuleName = "Module", EntityName = "Template" },
                Choice = "Bump",
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
            "Bump",
            DamlUnit.Instance);

        var client = CreateClient();

        var action = () => client.TryExerciseAsync<object>(exerciseCommand, "party::alice", cancellationToken: TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Bump*");
    }

    [Fact]
    public async Task TryExerciseAsync_propagates_cancellation()
    {
        var ex = new RpcException(new Status(StatusCode.Cancelled, "cancelled"));
        LedgerClientTestFixtures.StubCommandServiceFailure(_commandService, ex);

        var exerciseCommand = new RuntimeCommands.ExerciseCommand(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            "00contract123",
            "Archive",
            DamlUnit.Instance);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var client = CreateClient();

        var action = () => client.TryExerciseAsync<object>(exerciseCommand, "party::alice", cancellationToken: cts.Token);

        await action.Should().ThrowAsync<RpcException>(
            "a caller-cancelled exercise must surface as cancellation, not a mapped InfraError");
    }

    [Fact]
    public void LedgerClient_constructor_does_not_throw_when_ITokenProvider_None()
    {
        using var _ = new LedgerClient(_options, ITokenProvider.None);
    }

    [Fact]
    public void LedgerClient_constructor_does_not_throw_when_real_provider_registered()
    {
        using var _ = new LedgerClient(_options, _tokenProvider);
    }

    [Fact]
    public async Task TryExerciseAsync_returns_DamlError_on_structured_failure()
    {
        var ex = LedgerClientTestFixtures.MakeDamlRpcException(
            "CONTRACT_NOT_FOUND",
            "contract not found",
            "InvalidGivenCurrentSystemStateOther");
        LedgerClientTestFixtures.StubCommandServiceFailure(_commandService, ex);

        var exerciseCommand = new RuntimeCommands.ExerciseCommand(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            "00contract123",
            "Archive",
            DamlUnit.Instance);

        var client = CreateClient();
        var outcome = await client.TryExerciseAsync<object>(
            exerciseCommand, "party::alice", cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<object>.DamlError>();
        var err = (ExerciseOutcome<object>.DamlError)outcome;
        err.Category.Should().Be(DamlErrorCategory.InvalidGivenCurrentSystemStateOther);
        err.ErrorId.Should().Be("CONTRACT_NOT_FOUND");
        err.Message.Should().Be("contract not found");
    }

    [Fact]
    public async Task TryExerciseAsync_returns_InfraError_on_unstructured_failure()
    {
        var ex = new RpcException(new Status(StatusCode.Unavailable, "network down"));
        LedgerClientTestFixtures.StubCommandServiceFailure(_commandService, ex);

        var exerciseCommand = new RuntimeCommands.ExerciseCommand(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            "00contract123",
            "Archive",
            DamlUnit.Instance);

        var client = CreateClient();
        var outcome = await client.TryExerciseAsync<object>(
            exerciseCommand, "party::alice", cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<object>.InfraError>();
        var infra = (ExerciseOutcome<object>.InfraError)outcome;
        infra.StatusCode.Should().Be((int)StatusCode.Unavailable);
        infra.Message.Should().Be("network down");
    }

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
