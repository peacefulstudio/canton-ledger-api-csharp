// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Canton.Ledger.Auth;
using Com.Daml.Ledger.Api.V2;
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
    public void to_proto_identifier_converts_correctly()
    {
        var identifier = new RuntimeIdentifier("package-id", "Module.Name", "Entity");

        var protoIdentifier = LedgerClient.ToProtoIdentifier(identifier);

        protoIdentifier.PackageId.Should().Be("package-id");
        protoIdentifier.ModuleName.Should().Be("Module.Name");
        protoIdentifier.EntityName.Should().Be("Entity");
    }

    [Fact]
    public void to_proto_value_converts_unit()
    {
        var value = DamlUnit.Instance;

        var protoValue = LedgerClient.ToProtoValue(value);

        protoValue.Unit.Should().NotBeNull();
    }

    [Fact]
    public void to_proto_value_converts_bool()
    {
        var protoTrue = LedgerClient.ToProtoValue(new DamlBool(true));
        var protoFalse = LedgerClient.ToProtoValue(new DamlBool(false));

        protoTrue.Bool.Should().BeTrue();
        protoFalse.Bool.Should().BeFalse();
    }

    [Fact]
    public void to_proto_value_converts_int64()
    {
        var value = new DamlInt64(42);

        var protoValue = LedgerClient.ToProtoValue(value);

        protoValue.Int64.Should().Be(42);
    }

    [Fact]
    public void to_proto_value_converts_text()
    {
        var value = new DamlText("hello world");

        var protoValue = LedgerClient.ToProtoValue(value);

        protoValue.Text.Should().Be("hello world");
    }

    [Fact]
    public void to_proto_value_converts_party()
    {
        var value = new DamlParty("party::alice");

        var protoValue = LedgerClient.ToProtoValue(value);

        protoValue.Party.Should().Be("party::alice");
    }

    [Fact]
    public void to_proto_value_converts_numeric()
    {
        var value = new DamlNumeric(123.456m);

        var protoValue = LedgerClient.ToProtoValue(value);

        protoValue.Numeric.Should().Be("123.456");
    }

    [Fact]
    public void to_proto_value_converts_date()
    {
        var date = new DateOnly(2024, 1, 1);
        var value = new DamlDate(date);

        var protoValue = LedgerClient.ToProtoValue(value);

        protoValue.Date.Should().Be(value.DaysSinceEpoch);
    }

    [Fact]
    public void to_proto_value_converts_timestamp()
    {
        var timestamp = DateTimeOffset.UnixEpoch.AddSeconds(1704067200);
        var value = new DamlTimestamp(timestamp);

        var protoValue = LedgerClient.ToProtoValue(value);

        protoValue.Timestamp.Should().Be(value.MicrosecondsSinceEpoch);
    }

    [Fact]
    public void to_proto_value_converts_record()
    {
        var record = new DamlRecord(
            new RuntimeIdentifier("pkg", "Module", "Record"),
            [
                new DamlField("name", new DamlText("Alice")),
                new DamlField("age", new DamlInt64(30))
            ]);

        var protoValue = LedgerClient.ToProtoValue(record);

        protoValue.Record.Should().NotBeNull();
        protoValue.Record.RecordId.PackageId.Should().Be("pkg");
        protoValue.Record.Fields.Should().HaveCount(2);
        protoValue.Record.Fields[0].Label.Should().Be("name");
        protoValue.Record.Fields[0].Value.Text.Should().Be("Alice");
        protoValue.Record.Fields[1].Label.Should().Be("age");
        protoValue.Record.Fields[1].Value.Int64.Should().Be(30);
    }

    [Fact]
    public void to_proto_value_converts_variant()
    {
        var variant = new DamlVariant(
            new RuntimeIdentifier("pkg", "Module", "Variant"),
            "Some",
            new DamlText("value"));

        var protoValue = LedgerClient.ToProtoValue(variant);

        protoValue.Variant.Should().NotBeNull();
        protoValue.Variant.Constructor.Should().Be("Some");
        protoValue.Variant.Value.Text.Should().Be("value");
        protoValue.Variant.VariantId.PackageId.Should().Be("pkg");
    }

    [Fact]
    public void to_proto_value_converts_list()
    {
        var list = new DamlList([
            new DamlInt64(1),
            new DamlInt64(2),
            new DamlInt64(3)
        ]);

        var protoValue = LedgerClient.ToProtoValue(list);

        protoValue.List.Should().NotBeNull();
        protoValue.List.Elements.Should().HaveCount(3);
        protoValue.List.Elements[0].Int64.Should().Be(1);
        protoValue.List.Elements[1].Int64.Should().Be(2);
        protoValue.List.Elements[2].Int64.Should().Be(3);
    }

    [Fact]
    public void to_proto_value_converts_optional_with_value()
    {
        var optional = new DamlOptional(new DamlText("present"));

        var protoValue = LedgerClient.ToProtoValue(optional);

        protoValue.Optional.Should().NotBeNull();
        protoValue.Optional.Value.Text.Should().Be("present");
    }

    [Fact]
    public void to_proto_value_converts_optional_without_value()
    {
        var optional = new DamlOptional(null);

        var protoValue = LedgerClient.ToProtoValue(optional);

        protoValue.Optional.Should().NotBeNull();
        protoValue.Optional.Value.Should().BeNull();
    }

    [Fact]
    public void to_proto_value_converts_text_map()
    {
        var map = new DamlTextMap(new Dictionary<string, DamlValue>
        {
            ["key1"] = new DamlText("value1"),
            ["key2"] = new DamlText("value2")
        });

        var protoValue = LedgerClient.ToProtoValue(map);

        protoValue.TextMap.Should().NotBeNull();
        protoValue.TextMap.Entries.Should().HaveCount(2);
    }

    [Fact]
    public void to_proto_value_converts_gen_map()
    {
        var map = new DamlGenMap([
            (new DamlInt64(1), new DamlText("one")),
            (new DamlInt64(2), new DamlText("two"))
        ]);

        var protoValue = LedgerClient.ToProtoValue(map);

        protoValue.GenMap.Should().NotBeNull();
        protoValue.GenMap.Entries.Should().HaveCount(2);
        protoValue.GenMap.Entries[0].Key.Int64.Should().Be(1);
        protoValue.GenMap.Entries[0].Value.Text.Should().Be("one");
    }

    [Fact]
    public void to_proto_value_converts_enum()
    {
        var enumValue = new DamlEnum(
            new RuntimeIdentifier("pkg", "Module", "Color"),
            "Red");

        var protoValue = LedgerClient.ToProtoValue(enumValue);

        protoValue.Enum.Should().NotBeNull();
        protoValue.Enum.Constructor.Should().Be("Red");
        protoValue.Enum.EnumId.PackageId.Should().Be("pkg");
    }

    [Fact]
    public void to_proto_record_converts_correctly()
    {
        var record = new DamlRecord(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            [
                new DamlField("owner", new DamlParty("party::alice")),
                new DamlField("value", new DamlInt64(100))
            ]);

        var protoRecord = LedgerClient.ToProtoRecord(record);

        protoRecord.RecordId.Should().NotBeNull();
        protoRecord.RecordId.PackageId.Should().Be("pkg");
        protoRecord.RecordId.ModuleName.Should().Be("Module");
        protoRecord.RecordId.EntityName.Should().Be("Template");
        protoRecord.Fields.Should().HaveCount(2);
    }

    [Fact]
    public void to_proto_record_handles_null_record_id()
    {
        var record = new DamlRecord(null, [
            new DamlField("field", new DamlText("value"))
        ]);

        var protoRecord = LedgerClient.ToProtoRecord(record);

        protoRecord.RecordId.Should().BeNull();
        protoRecord.Fields.Should().ContainSingle();
    }

    [Fact]
    public void build_commands_sets_command_id_and_workflow_id()
    {
        var createCommand = new RuntimeCommands.CreateCommand(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            new DamlRecord(null, []));

        var submission = RuntimeCommands.CommandsSubmission.Single(createCommand)
            .WithActAs("party::alice")
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
            .WithActAs("party::alice");

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
            .WithActAs("party::alice")
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
            .WithActAs("party::alice")
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
            .WithActAs("party::alice")
            .WithReadAs("party::observer1", "party::observer2")
            .WithCommandId("test-cmd");

        var client = CreateClient();
        var commands = client.BuildCommands(submission);

        commands.ReadAs.Should().HaveCount(2);
        commands.ReadAs.Should().Contain("party::observer1");
        commands.ReadAs.Should().Contain("party::observer2");
    }

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
            .WithActAs("party::alice")
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
            Created = new CreatedEvent
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
            .WithActAs("party::alice")
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
            Archived = new ArchivedEvent
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
            .WithActAs("party::alice")
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
            .WithActAs("party::alice")
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
            .WithActAs("party::alice")
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
}
