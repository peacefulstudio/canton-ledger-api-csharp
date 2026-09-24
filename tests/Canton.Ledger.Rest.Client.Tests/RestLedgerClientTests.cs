// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Serialization;
using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Stdlib;
using Microsoft.Extensions.Options;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestLedgerClientTests : IDisposable
{
    private static readonly Party Alice = new("party::alice");
    private static readonly Party Bob = new("party::bob");

    private readonly List<StubHttpClientFactory> _factories = [];

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    private StubHttpClientFactory TrackedFactory(RecordingHttpHandler transport)
    {
        var factory = new StubHttpClientFactory(transport);
        _factories.Add(factory);
        return factory;
    }

    private sealed record TestTemplate([property: DamlFieldAttribute("owner")] Party Owner) : ITemplate, IDamlRecord<TestTemplate>
    {
        public TestTemplate()
            : this(Alice)
        {
        }

        public static RuntimeIdentifier TemplateId { get; } = new("pkg", "Module", "LedgerClientTemplate");
        public static string PackageId => "pkg";
        public static string PackageName => "pkg-name";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => new(TemplateId, [new DamlField("owner", Owner.ToDamlValue())]);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(
                json,
                context,
                ("owner", DamlLfJsonDecoders.ReadParty));
        public static TestTemplate FromRecord(DamlRecord record) =>
            new(Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()));

        public static Choice<TestTemplate, DamlUnit, Party> ChoiceGetOwner { get; } = new()
        {
            Name = new ChoiceName("GetOwner"),
            Consuming = false,
            ArgumentEncoder = unit => unit,
            ResultDecoder = result => Party.FromDamlValue(result.As<DamlParty>()),
            ArgumentDecoder = value => value.As<DamlUnit>(),
            ArgumentJsonReader = DamlLfJsonDecoders.ReadUnit,
            ResultJsonReader = DamlLfJsonDecoders.ReadParty,
        };

        public static Choice<TestTemplate, DamlUnit, Optional<Party>> ChoiceFindOwner { get; } = new()
        {
            Name = new ChoiceName("FindOwner"),
            Consuming = false,
            ArgumentEncoder = unit => unit,
            ResultDecoder = _ => new Optional<Party>.None(),
            ArgumentDecoder = value => value.As<DamlUnit>(),
            ArgumentJsonReader = DamlLfJsonDecoders.ReadUnit,
            ResultJsonReader = (json, context) => DamlLfJsonDecoders.ReadOptional(json, context, DamlLfJsonDecoders.ReadParty),
        };

        public static Choice<TestTemplate, DamlUnit, IReadOnlyList<ContractId<TestTemplate>>> ChoiceSplitHoldings { get; } = new()
        {
            Name = new ChoiceName("SplitHoldings"),
            Consuming = true,
            ArgumentEncoder = unit => unit,
            ResultDecoder = _ => [],
            ArgumentDecoder = value => value.As<DamlUnit>(),
            ArgumentJsonReader = DamlLfJsonDecoders.ReadUnit,
            ResultJsonReader = (json, context) => DamlLfJsonDecoders.ReadList(json, context, DamlLfJsonDecoders.ReadContractId),
        };
    }

    private RestLedgerClient ClientWith(RecordingHttpHandler transport, string? userId = null) =>
        new(TrackedFactory(transport), Options.Create(new RestLedgerClientOptions
        {
            HttpAddress = "http://localhost:7575",
            UserId = userId,
        }));

    [Theory]
    [InlineData("""{"offset":42}""", 42L)]
    [InlineData("""{"offset":0}""", 0L)]
    [InlineData("""{"offset":"9695"}""", 9695L)]
    [InlineData("""{"offset":"0"}""", 0L)]
    [InlineData("""{"offset":9007199254740993}""", 9007199254740993L)]
    [InlineData("""{"offset":"9007199254740993"}""", 9007199254740993L)]
    public async Task GetLedgerEndAsync_binds_the_offset_from_v2_state_ledger_end_in_either_wire_encoding(
        string responseBody,
        long expectedOffset)
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, responseBody);
        var client = new RestLedgerClient(TrackedFactory(transport));

        var offset = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        offset.Value.Should().Be(expectedOffset);
        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/state/ledger-end");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"offset":null}""")]
    public async Task GetLedgerEndAsync_reads_a_body_supplying_no_offset_as_the_empty_ledger(
        string responseBody)
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, responseBody);
        var client = new RestLedgerClient(TrackedFactory(transport));

        var offset = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        offset.Value.Should().Be(0L);
    }

    [Theory]
    [InlineData("""{"offset":"not-an-offset"}""")]
    [InlineData("""{"offset":"-1"}""")]
    [InlineData("""{"offset":""}""")]
    public async Task GetLedgerEndAsync_rejects_an_offset_that_is_present_and_not_a_non_negative_integer(
        string responseBody)
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, responseBody);
        var client = new RestLedgerClient(TrackedFactory(transport));

        var act = () => client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<Daml.Ledger.Abstractions.LedgerOperationException>();
        thrown.Which.Message.Should().Contain("ledger end offset was not a non-negative integer");
    }

    [Fact]
    public async Task GetLedgerEndAsync_rejects_a_successful_response_carrying_no_body()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, "null");
        var client = new RestLedgerClient(TrackedFactory(transport));

        var act = () => client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<Daml.Ledger.Abstractions.LedgerOperationException>();
        thrown.Which.Message.Should().Contain("no body was present for the ledger end");
    }

    [Fact]
    public async Task GetLedgerEndAsync_surfaces_the_category_and_error_id_of_a_JsCantonError_response()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.BadRequest,
            """
            {
              "code": "INVALID_ARGUMENT",
              "cause": "the ledger end is not available while the participant is replaying",
              "context": {"category": "8"},
              "errorCategory": 8
            }
            """);
        var client = new RestLedgerClient(TrackedFactory(transport));

        var act = () => client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<Daml.Ledger.Abstractions.LedgerOperationException>();
        thrown.Which.ErrorId.Should().Be("INVALID_ARGUMENT");
        thrown.Which.Category.Should().Be(DamlErrorCategory.InvalidIndependentOfSystemState);
        thrown.Which.Message.Should().Be("the ledger end is not available while the participant is replaying");
    }

    [Fact]
    public async Task GetLedgerEndAsync_keeps_the_participants_message_and_status_code_for_an_error_without_an_id()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.ServiceUnavailable,
            """{"code": 14, "message": "participant is shutting down"}""");
        var client = new RestLedgerClient(TrackedFactory(transport));

        var act = () => client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<Daml.Ledger.Abstractions.LedgerOperationException>();
        thrown.Which.Message.Should().Be("participant is shutting down");
        thrown.Which.StatusCode.Should().Be((int)HttpStatusCode.ServiceUnavailable);
        thrown.Which.ErrorId.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_posts_the_commands_and_projects_a_successful_transaction()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """
            {
              "transaction": {
                "updateId": "upd-1",
                "commandId": "cmd-1",
                "offset": "7",
                "events": [
                  {
                    "CreatedEvent": {
                      "offset": "7",
                      "contractId": "00holding",
                      "nodeId": 0,
                      "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "LedgerClientTemplate"},
                      "createArgument": {"owner": "party::alice"}
                    }
                  }
                ]
              }
            }
            """);
        var client = ClientWith(transport, userId: "test-user");
        var submission = CommandsSubmission.Single(CreateCommand.For(new TestTemplate()))
            .WithActAs(Alice)
            .WithCommandId(new CommandId("cmd-1"));

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            submission, cancellationToken: TestContext.Current.CancellationToken);

        var success = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>().Subject;
        success.Result.UpdateId.Should().Be("upd-1");
        success.Result.CreatedContracts.Should().ContainSingle().Which.ContractId.Should().Be("00holding");

        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/commands/submit-and-wait-for-transaction");
        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        var commands = body.RootElement.GetProperty("commands");
        commands.GetProperty("commandId").GetString().Should().Be("cmd-1");
        commands.GetProperty("userId").GetString().Should().Be("test-user");
        commands.GetProperty("actAs")[0].GetString().Should().Be("party::alice");
        commands.GetProperty("commands")[0].GetProperty("CreateCommand").GetProperty("templateId")
            .GetString().Should().Be("pkg:Module:LedgerClientTemplate");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_returns_a_DamlError_outcome_on_a_structured_error_response()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.Conflict,
            """
            {
              "code": 9,
              "message": "DUPLICATE_COMMAND",
              "details": [
                {
                  "@type": "type.googleapis.com/google.rpc.ErrorInfo",
                  "reason": "DUPLICATE_COMMAND",
                  "metadata": {"category": "ContentionOnSharedResources"}
                }
              ]
            }
            """);
        var client = ClientWith(transport);
        var submission = CommandsSubmission.Single(CreateCommand.For(new TestTemplate())).WithActAs(Alice);

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            submission, cancellationToken: TestContext.Current.CancellationToken);

        var error = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.DamlError>().Subject;
        error.Category.Should().Be(DamlErrorCategory.ContentionOnSharedResources);
        error.ErrorId.Should().Be("DUPLICATE_COMMAND");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_returns_an_InfraError_outcome_when_the_transport_fails()
    {
        var transport = new RecordingHttpHandler().WithTransportException(new HttpRequestException("connection refused"));
        var client = ClientWith(transport);
        var submission = CommandsSubmission.Single(CreateCommand.For(new TestTemplate())).WithActAs(Alice);

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            submission, cancellationToken: TestContext.Current.CancellationToken);

        var error = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.InfraError>().Subject;
        error.Message.Should().Contain("connection refused");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_returns_a_CommittedUndecodable_outcome_for_a_malformed_transaction_body()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """
            {
              "transaction": {
                "updateId": "upd-1",
                "offset": "not-a-number",
                "events": []
              }
            }
            """);
        var client = ClientWith(transport);
        var submission = CommandsSubmission.Single(CreateCommand.For(new TestTemplate())).WithActAs(Alice);

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            submission, cancellationToken: TestContext.Current.CancellationToken);

        var error = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.CommittedUndecodable>().Subject;
        error.UpdateId.Should().Be("upd-1");
        error.SourceException.Should().NotBeNull();
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_returns_a_CommittedUndecodable_outcome_for_a_created_event_missing_its_template_id()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """
            {
              "transaction": {
                "updateId": "upd-1",
                "offset": "1",
                "events": [
                  {
                    "CreatedEvent": {
                      "offset": "1",
                      "contractId": "00holding",
                      "nodeId": 0,
                      "createArgument": {}
                    }
                  }
                ]
              }
            }
            """);
        var client = ClientWith(transport);
        var submission = CommandsSubmission.Single(CreateCommand.For(new TestTemplate())).WithActAs(Alice);

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            submission, cancellationToken: TestContext.Current.CancellationToken);

        var error = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.CommittedUndecodable>().Subject;
        error.UpdateId.Should().Be("upd-1");
        error.Message.Should().Contain("templateId");
        error.SourceException.Should().BeOfType<MalformedResponseException>();
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_returns_a_CommittedUndecodable_outcome_for_a_created_event_whose_createArgument_cannot_be_decoded()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """
            {
              "transaction": {
                "updateId": "upd-1",
                "offset": "1",
                "events": [
                  {
                    "CreatedEvent": {
                      "offset": "1",
                      "contractId": "00holding",
                      "nodeId": 0,
                      "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "LedgerClientTemplate"},
                      "createArgument": {"owner": [null]}
                    }
                  }
                ]
              }
            }
            """);
        var client = ClientWith(transport);
        var submission = CommandsSubmission.Single(CreateCommand.For(new TestTemplate())).WithActAs(Alice);

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            submission, cancellationToken: TestContext.Current.CancellationToken);

        var error = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.CommittedUndecodable>().Subject;
        error.UpdateId.Should().Be("upd-1");
        error.Message.Should().Contain("Expected JSON String at 'TestTemplate.owner' but found Array");
        error.SourceException.Should().NotBeNull();
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_returns_a_CommittedUndecodable_outcome_for_a_malformed_json_response_body()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, "{not valid json");
        var client = ClientWith(transport);
        var submission = CommandsSubmission.Single(CreateCommand.For(new TestTemplate())).WithActAs(Alice);

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            submission, cancellationToken: TestContext.Current.CancellationToken);

        var error = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.CommittedUndecodable>().Subject;
        error.UpdateId.Should().BeNull("the body was not decoded far enough to read an update id");
        error.SourceException.Should().NotBeNull();
    }

    [Fact]
    public async Task SubmitAndWaitAsync_throws_a_LedgerOperationException_for_a_malformed_json_response_body()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, "{not valid json");
        var client = ClientWith(transport);
        var submission = CommandsSubmission.Single(CreateCommand.For(new TestTemplate())).WithActAs(Alice);

        var act = () => client.SubmitAndWaitAsync(submission, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<Daml.Ledger.Abstractions.LedgerOperationException>();
    }

    [Fact]
    public async Task SubmitAndWaitAsync_posts_the_commands_and_returns_the_update_id_and_completion_offset()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK, """{"updateId": "upd-1", "completionOffset": "9"}""");
        var client = ClientWith(transport);
        var submission = CommandsSubmission.Single(CreateCommand.For(new TestTemplate()))
            .WithActAs(Alice)
            .WithCommandId(new CommandId("cmd-9"));

        var result = await client.SubmitAndWaitAsync(submission, cancellationToken: TestContext.Current.CancellationToken);

        result.UpdateId.Should().Be("upd-1");
        result.CompletionOffset.Value.Should().Be(9L);
        result.CommandId.Value.Should().Be("cmd-9");
        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/commands/submit-and-wait");
    }

    [Fact]
    public async Task SubmitAndWaitAsync_throws_a_LedgerOperationException_on_a_structured_error_response()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.BadRequest,
            """
            {
              "code": 3,
              "message": "invalid argument",
              "details": [
                {"@type": "type.googleapis.com/google.rpc.ErrorInfo", "reason": "INVALID_ARGUMENT", "metadata": {}}
              ]
            }
            """);
        var client = ClientWith(transport);
        var submission = CommandsSubmission.Single(CreateCommand.For(new TestTemplate())).WithActAs(Alice);

        var act = () => client.SubmitAndWaitAsync(submission, cancellationToken: TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<Daml.Ledger.Abstractions.LedgerOperationException>();
        thrown.Which.ErrorId.Should().Be("INVALID_ARGUMENT");
    }

    [Fact]
    public async Task SubmitAndWaitAsync_surfaces_the_category_and_error_id_of_a_JsCantonError_response()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.BadRequest,
            """
            {
              "code": "INVALID_ARGUMENT",
              "cause": "source and target synchronizers are the same",
              "context": {"category": "8"},
              "errorCategory": 8
            }
            """);
        var client = ClientWith(transport);
        var submission = CommandsSubmission.Single(CreateCommand.For(new TestTemplate())).WithActAs(Alice);

        var act = () => client.SubmitAndWaitAsync(submission, cancellationToken: TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<Daml.Ledger.Abstractions.LedgerOperationException>();
        thrown.Which.ErrorId.Should().Be("INVALID_ARGUMENT");
        thrown.Which.Category.Should().Be(DamlErrorCategory.InvalidIndependentOfSystemState);
        thrown.Which.Message.Should().Be("source and target synchronizers are the same");
    }

    [Fact]
    public async Task SubmitAndWaitAsync_wraps_a_transport_failure_in_a_LedgerOperationException()
    {
        var transport = new RecordingHttpHandler().WithTransportException(new HttpRequestException("connection refused"));
        var client = ClientWith(transport);
        var submission = CommandsSubmission.Single(CreateCommand.For(new TestTemplate())).WithActAs(Alice);

        var act = () => client.SubmitAndWaitAsync(submission, cancellationToken: TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<Daml.Ledger.Abstractions.LedgerOperationException>();
        thrown.Which.StatusCode.Should().Be((int)HttpStatusCode.ServiceUnavailable);
        thrown.Which.Message.Should().Contain("connection refused");
    }

    [Fact]
    public async Task SubmitAndWaitAsync_wraps_a_client_timeout_in_a_LedgerOperationException()
    {
        var transport = new RecordingHttpHandler().WithTransportException(
            new TaskCanceledException("timed out", new TimeoutException()));
        var client = ClientWith(transport);
        var submission = CommandsSubmission.Single(CreateCommand.For(new TestTemplate())).WithActAs(Alice);

        var act = () => client.SubmitAndWaitAsync(
            submission, timeout: TimeSpan.FromSeconds(1), cancellationToken: TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<Daml.Ledger.Abstractions.LedgerOperationException>();
        thrown.Which.StatusCode.Should().Be((int)HttpStatusCode.RequestTimeout);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-number")]
    public async Task SubmitAndWaitAsync_throws_a_LedgerOperationException_for_an_unparseable_completion_offset(
        string completionOffset)
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK, $$"""{"updateId": "upd-1", "completionOffset": "{{completionOffset}}"}""");
        var client = ClientWith(transport);
        var submission = CommandsSubmission.Single(CreateCommand.For(new TestTemplate())).WithActAs(Alice);

        var act = () => client.SubmitAndWaitAsync(submission, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<Daml.Ledger.Abstractions.LedgerOperationException>();
    }

    [Fact]
    public async Task TryCreateAsync_creates_the_contract_and_projects_its_contract_id()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """
            {
              "transaction": {
                "updateId": "upd-1",
                "offset": "1",
                "events": [
                  {
                    "CreatedEvent": {
                      "offset": "1",
                      "contractId": "00holding",
                      "nodeId": 0,
                      "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "LedgerClientTemplate"},
                      "createArgument": {"owner": "party::alice"}
                    }
                  }
                ]
              }
            }
            """);
        var client = ClientWith(transport);

        var outcome = await client.TryCreateAsync(
            new TestTemplate(), Alice, cancellationToken: TestContext.Current.CancellationToken);

        var one = outcome.Should().BeOfType<ExerciseOutcome<ContractId<TestTemplate>>.One>().Subject;
        one.Result.Value.Should().Be("00holding");
    }

    private const string GetOwnerExercisedTransactionResponse =
        """
        {
          "transaction": {
            "updateId": "upd-1",
            "offset": "1",
            "events": [
              {
                "ExercisedEvent": {
                  "offset": "1",
                  "contractId": "00holding",
                  "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "LedgerClientTemplate"},
                  "choice": "GetOwner",
                  "choiceArgument": {},
                  "actingParties": ["party::alice"],
                  "consuming": false,
                  "witnessParties": ["party::alice"],
                  "exerciseResult": "party::alice"
                }
              }
            ]
          }
        }
        """;

    [Fact]
    public async Task TryExerciseAsync_exercises_the_choice_and_projects_the_typed_result()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, GetOwnerExercisedTransactionResponse);
        var client = ClientWith(transport);
        var command = ExerciseCommand.For(
            new ContractId<TestTemplate>("00holding"), new ChoiceName("GetOwner"), DamlUnit.Instance);

        var outcome = await client.TryExerciseAsync<Party>(
            command, Alice, cancellationToken: TestContext.Current.CancellationToken);

        var one = outcome.Should().BeOfType<ExerciseOutcome<Party>.One>().Subject;
        one.Result.Should().Be(Alice);
    }

    [Fact]
    public async Task TryExerciseAsync_returns_CommittedUndecodable_when_the_result_type_has_no_Daml_mapping()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, GetOwnerExercisedTransactionResponse);
        var client = ClientWith(transport);
        var command = ExerciseCommand.For(
            new ContractId<TestTemplate>("00holding"), new ChoiceName("GetOwner"), DamlUnit.Instance);

        var outcome = await client.TryExerciseAsync<ResultTypeWithoutDamlMapping>(
            command, Alice, cancellationToken: TestContext.Current.CancellationToken);

        var undecodable = outcome.Should().BeOfType<ExerciseOutcome<ResultTypeWithoutDamlMapping>.CommittedUndecodable>().Subject;
        undecodable.UpdateId.Should().Be("upd-1");
        undecodable.Message.Should().StartWith("The command committed, but its choice result could not be read: ");
        undecodable.SourceException.Should().BeOfType<NotSupportedException>();
    }

    private sealed class ResultTypeWithoutDamlMapping;

    private const string MissingTemplateExercisedTransactionResponse =
        """
        {
          "transaction": {
            "updateId": "upd-1",
            "offset": "1",
            "events": [
              {
                "ExercisedEvent": {
                  "offset": "1",
                  "contractId": "00holding",
                  "templateId": {"packageId": "missing-pkg", "moduleName": "Missing.Module", "entityName": "Missing"},
                  "choice": "GetOwner",
                  "choiceArgument": {},
                  "actingParties": ["party::alice"],
                  "consuming": false,
                  "witnessParties": ["party::alice"],
                  "exerciseResult": "party::alice"
                }
              }
            ]
          }
        }
        """;

    private const string MissingTemplateCreatedTransactionResponse =
        """
        {
          "transaction": {
            "updateId": "upd-1",
            "offset": "1",
            "events": [
              {
                "CreatedEvent": {
                  "offset": "1",
                  "contractId": "00holding",
                  "nodeId": 0,
                  "templateId": {"packageId": "missing-pkg", "moduleName": "Missing.Module", "entityName": "Missing"},
                  "createArgument": {"owner": "party::alice"}
                }
              }
            ]
          }
        }
        """;

    [Fact]
    public async Task TryExerciseAsync_returns_CommittedUndecodable_when_the_exercised_template_has_no_loaded_generated_type()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, MissingTemplateExercisedTransactionResponse);
        var client = ClientWith(transport);
        var command = ExerciseCommand.For(
            new ContractId<TestTemplate>("00holding"), new ChoiceName("GetOwner"), DamlUnit.Instance);

        var outcome = await client.TryExerciseAsync<Party>(
            command, Alice, cancellationToken: TestContext.Current.CancellationToken);

        var error = outcome.Should().BeOfType<ExerciseOutcome<Party>.CommittedUndecodable>().Subject;
        error.UpdateId.Should().Be("upd-1");
        error.Message.Should().Be(
            "The command committed, but its transaction could not be decoded: No generated type is loaded for choice 'GetOwner' of 'missing-pkg:Missing.Module:Missing'; load exactly one assembly generated for its Daml package before reading this payload over the JSON Ledger API.");
        error.SourceException.Should().BeOfType<TemplateTypeRequiredException>()
            .Which.TypeId.Should().Be("missing-pkg:Missing.Module:Missing");
    }

    [Fact]
    public async Task TryCreateAsync_returns_CommittedUndecodable_when_the_created_template_has_no_loaded_generated_type()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, MissingTemplateCreatedTransactionResponse);
        var client = ClientWith(transport);

        var outcome = await client.TryCreateAsync(
            new TestTemplate(), Alice, cancellationToken: TestContext.Current.CancellationToken);

        var error = outcome.Should().BeOfType<ExerciseOutcome<ContractId<TestTemplate>>.CommittedUndecodable>().Subject;
        error.UpdateId.Should().Be("upd-1");
        error.Message.Should().Be(
            "The command committed, but its transaction could not be decoded: No generated type is loaded for 'missing-pkg:Missing.Module:Missing'; load exactly one assembly generated for its Daml package before reading this payload over the JSON Ledger API.");
        error.SourceException.Should().BeOfType<TemplateTypeRequiredException>()
            .Which.TypeId.Should().Be("missing-pkg:Missing.Module:Missing");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_returns_CommittedUndecodable_when_the_created_template_has_no_loaded_generated_type()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, MissingTemplateCreatedTransactionResponse);
        var client = ClientWith(transport);
        var submission = CommandsSubmission.Single(CreateCommand.For(new TestTemplate())).WithActAs(Alice);

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            submission, cancellationToken: TestContext.Current.CancellationToken);

        var error = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.CommittedUndecodable>().Subject;
        error.UpdateId.Should().Be("upd-1");
        error.Message.Should().Be(
            "The command committed, but its transaction could not be decoded: No generated type is loaded for 'missing-pkg:Missing.Module:Missing'; load exactly one assembly generated for its Daml package before reading this payload over the JSON Ledger API.");
        error.SourceException.Should().BeOfType<TemplateTypeRequiredException>();
    }

    [Fact]
    public async Task OneOrThrowAsync_over_TryExerciseAsync_throws_a_LedgerOperationException_carrying_the_refusal_after_the_commit()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, MissingTemplateExercisedTransactionResponse);
        var client = ClientWith(transport);
        var command = ExerciseCommand.For(
            new ContractId<TestTemplate>("00holding"), new ChoiceName("GetOwner"), DamlUnit.Instance);

        var thrown = await Record.ExceptionAsync(() => client.TryExerciseAsync<Party>(
                command, Alice, cancellationToken: TestContext.Current.CancellationToken)
            .OneOrThrowAsync("GetOwner"));

        var failure = thrown.Should().BeOfType<LedgerOperationException>().Subject;
        failure.Message.Should().Be(
            "GetOwner: committed but undecodable: The command committed, but its transaction could not be decoded: No generated type is loaded for choice 'GetOwner' of 'missing-pkg:Missing.Module:Missing'; load exactly one assembly generated for its Daml package before reading this payload over the JSON Ledger API.");
        failure.InnerException.Should().BeOfType<TemplateTypeRequiredException>();
        failure.UpdateId.Should().Be("upd-1");
        failure.CommitState.Should().Be(CommitState.Committed);
    }

    [Fact]
    public async Task TryExerciseAsync_decodes_a_top_level_list_choice_result_as_a_DamlList()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """
            {
              "transaction": {
                "updateId": "upd-1",
                "offset": "1",
                "events": [
                  {
                    "ExercisedEvent": {
                      "offset": "1",
                      "contractId": "00holding",
                      "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "LedgerClientTemplate"},
                      "choice": "SplitHoldings",
                      "choiceArgument": {},
                      "actingParties": ["party::alice"],
                      "consuming": true,
                      "witnessParties": ["party::alice"],
                      "exerciseResult": ["00abc"]
                    }
                  }
                ]
              }
            }
            """);
        var client = ClientWith(transport);
        var command = ExerciseCommand.For(
            new ContractId<TestTemplate>("00holding"), new ChoiceName("SplitHoldings"), DamlUnit.Instance);

        var outcome = await client.TryExerciseAsync<DamlList>(
            command, Alice, cancellationToken: TestContext.Current.CancellationToken);

        var list = outcome.Should().BeOfType<ExerciseOutcome<DamlList>.One>().Subject.Result;
        list.Values.Should().HaveCount(1);
        list.Values[0].Should().BeOfType<DamlContractId>().Which.Value.Should().Be("00abc");
    }

    private const string ExercisedTransactionResponse =
        """
        {
          "transaction": {
            "updateId": "upd-1",
            "offset": "1",
            "events": [
              {
                "ExercisedEvent": {
                  "offset": "1",
                  "contractId": "00holding",
                  "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "LedgerClientTemplate"},
                  "choice": "GetOwner",
                  "choiceArgument": {},
                  "actingParties": ["party::alice"],
                  "consuming": false,
                  "witnessParties": ["party::alice"],
                  "exerciseResult": "party::alice"
                }
              }
            ]
          }
        }
        """;

    private const string CreatedTransactionResponse =
        """
        {
          "transaction": {
            "updateId": "upd-1",
            "offset": "1",
            "events": [
              {
                "CreatedEvent": {
                  "offset": "1",
                  "contractId": "00holding",
                  "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "LedgerClientTemplate"},
                  "createArgument": {"owner": "party::alice"}
                }
              }
            ]
          }
        }
        """;

    [Fact]
    public async Task TryExerciseAsync_uses_ledger_effects_shape()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, ExercisedTransactionResponse);
        var client = ClientWith(transport);
        var command = ExerciseCommand.For(
            new ContractId<TestTemplate>("00holding"), new ChoiceName("GetOwner"), DamlUnit.Instance);

        await client.TryExerciseAsync<Party>(
            command, Alice, cancellationToken: TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        var transactionFormat = body.RootElement.GetProperty("transactionFormat");
        transactionFormat.GetProperty("transactionShape").GetString()
            .Should().Be("TRANSACTION_SHAPE_LEDGER_EFFECTS");
        var eventFormat = transactionFormat.GetProperty("eventFormat");
        eventFormat.GetProperty("verbose").GetBoolean().Should().BeTrue();
        eventFormat.GetProperty("filtersByParty").GetProperty(Alice.Id)
            .GetProperty("cumulative").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task TryExerciseAsync_asks_for_ledger_effects_for_every_actAs_and_readAs_party()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, ExercisedTransactionResponse);
        var client = ClientWith(transport);
        var command = ExerciseCommand.For(
            new ContractId<TestTemplate>("00holding"), new ChoiceName("GetOwner"), DamlUnit.Instance);
        var submitter = new SubmitterInfo(Alice, new HashSet<Party> { Bob });

        await client.TryExerciseAsync<Party>(
            command, submitter, cancellationToken: TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        var filtersByParty = body.RootElement
            .GetProperty("transactionFormat").GetProperty("eventFormat").GetProperty("filtersByParty");
        filtersByParty.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo([Alice.Id, Bob.Id]);
    }

    [Fact]
    public async Task TryCreateAsync_uses_the_server_default_acs_delta_shape()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, CreatedTransactionResponse);
        var client = ClientWith(transport);

        await client.TryCreateAsync(
            new TestTemplate(), Alice, cancellationToken: TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        body.RootElement.TryGetProperty("transactionFormat", out _).Should().BeFalse(
            "the create path carries no shape of its own, so the participant applies its "
            + "stakeholder-scoped ACS-delta default");
    }

    [Fact]
    public async Task TryCreateAsync_posts_the_minted_submission_to_the_shared_submit_and_wait_for_transaction_call()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, CreatedTransactionResponse);
        var client = ClientWith(transport, userId: "test-user");

        await client.TryCreateAsync(
            new TestTemplate(), Alice, cancellationToken: TestContext.Current.CancellationToken);

        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be(
            "/v2/commands/submit-and-wait-for-transaction",
            "the create path mints its own submission and then hands it to the same submit-and-wait-for-transaction "
            + "call every other transaction submission uses");
        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        var commands = body.RootElement.GetProperty("commands");
        commands.GetProperty("userId").GetString().Should().Be("test-user");
        commands.GetProperty("actAs")[0].GetString().Should().Be("party::alice");
        commands.GetProperty("workflowId").GetString().Should().Be("create-testtemplate");
        commands.GetProperty("commands")[0].GetProperty("CreateCommand").GetProperty("templateId")
            .GetString().Should().Be("pkg:Module:LedgerClientTemplate");
    }

    [Fact]
    public async Task TryCreateAsync_returns_a_CommittedUndecodable_outcome_when_the_response_carries_no_transaction()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, "{}");
        var client = ClientWith(transport);

        var outcome = await client.TryCreateAsync(
            new TestTemplate(), Alice, cancellationToken: TestContext.Current.CancellationToken);

        var error = outcome.Should().BeOfType<ExerciseOutcome<ContractId<TestTemplate>>.CommittedUndecodable>().Subject;
        error.UpdateId.Should().BeNull();
        error.Message.Should().Be("Server returned a successful response but no transaction was present.");
        error.SourceException.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be("Server returned a successful response but no transaction was present.");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_uses_the_server_default_acs_delta_shape()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, CreatedTransactionResponse);
        var client = ClientWith(transport);
        var submission = CommandsSubmission.Single(CreateCommand.For(new TestTemplate())).WithActAs(Alice);

        await client.TrySubmitAndWaitForTransactionAsync(
            submission, cancellationToken: TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        body.RootElement.TryGetProperty("transactionFormat", out _).Should().BeFalse(
            "the plain submit path must keep the server-default ACS-delta shape so "
            + "ArchivedContractIds stays populated");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_with_a_submitter_uses_the_server_default_acs_delta_shape()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, CreatedTransactionResponse);
        var client = ClientWith(transport);
        var submission = CommandsSubmission.Single(CreateCommand.For(new TestTemplate()));

        await client.TrySubmitAndWaitForTransactionAsync(
            submission, Alice, cancellationToken: TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        body.RootElement.TryGetProperty("transactionFormat", out _).Should().BeFalse(
            "the plain submit path must keep the server-default ACS-delta shape so "
            + "ArchivedContractIds stays populated");
    }

    [Fact]
    public async Task TryExerciseAsync_decodes_an_explicit_null_exercise_result_as_an_empty_DamlOptional()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """
            {
              "transaction": {
                "updateId": "upd-1",
                "offset": "1",
                "events": [
                  {
                    "ExercisedEvent": {
                      "offset": "1",
                      "contractId": "00holding",
                      "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "LedgerClientTemplate"},
                      "choice": "FindOwner",
                      "choiceArgument": {},
                      "actingParties": ["party::alice"],
                      "consuming": false,
                      "witnessParties": ["party::alice"],
                      "exerciseResult": null
                    }
                  }
                ]
              }
            }
            """);
        var client = ClientWith(transport);
        var command = ExerciseCommand.For(
            new ContractId<TestTemplate>("00holding"), new ChoiceName("FindOwner"), DamlUnit.Instance);

        var outcome = await client.TryExerciseAsync<DamlOptional>(
            command, Alice, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<DamlOptional>.One>()
            .Which.Result.Value.Should().BeNull();
    }

    [Fact]
    public async Task TryExerciseAsync_returns_the_malformed_transaction_CommittedUndecodable_when_an_optional_exercise_result_is_absent()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """
            {
              "transaction": {
                "updateId": "upd-1",
                "offset": "1",
                "events": [
                  {
                    "ExercisedEvent": {
                      "offset": "1",
                      "contractId": "00holding",
                      "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "LedgerClientTemplate"},
                      "choice": "FindOwner",
                      "choiceArgument": {},
                      "actingParties": ["party::alice"],
                      "consuming": false,
                      "witnessParties": ["party::alice"]
                    }
                  }
                ]
              }
            }
            """);
        var client = ClientWith(transport);
        var command = ExerciseCommand.For(
            new ContractId<TestTemplate>("00holding"), new ChoiceName("FindOwner"), DamlUnit.Instance);

        var outcome = await client.TryExerciseAsync<Optional<Party>>(
            command, Alice, cancellationToken: TestContext.Current.CancellationToken);

        var error = outcome.Should().BeOfType<ExerciseOutcome<Optional<Party>>.CommittedUndecodable>().Subject;
        error.UpdateId.Should().Be("upd-1");
        error.Message.Should().Be(
            "Server returned a malformed transaction: Malformed response from ledger: ExercisedEvent for contract '00holding' has no exerciseResult, though the Ledger API marks the field as required.");
        error.SourceException.Should().BeOfType<MalformedResponseException>();
    }

    [Fact]
    public async Task TryExerciseAsync_returns_the_malformed_transaction_CommittedUndecodable_when_the_exercised_event_has_no_exercise_result()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """
            {
              "transaction": {
                "updateId": "upd-1",
                "offset": "1",
                "events": [
                  {
                    "ExercisedEvent": {
                      "offset": "1",
                      "contractId": "00holding",
                      "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "LedgerClientTemplate"},
                      "choice": "GetOwner",
                      "choiceArgument": {},
                      "actingParties": ["party::alice"],
                      "consuming": false,
                      "witnessParties": ["party::alice"]
                    }
                  }
                ]
              }
            }
            """);
        var client = ClientWith(transport);
        var command = ExerciseCommand.For(
            new ContractId<TestTemplate>("00holding"), new ChoiceName("GetOwner"), DamlUnit.Instance);

        var outcome = await client.TryExerciseAsync<Party>(
            command, Alice, cancellationToken: TestContext.Current.CancellationToken);

        var error = outcome.Should().BeOfType<ExerciseOutcome<Party>.CommittedUndecodable>().Subject;
        error.UpdateId.Should().Be("upd-1");
        error.Message.Should().Be(
            "Server returned a malformed transaction: Malformed response from ledger: ExercisedEvent for contract '00holding' has no exerciseResult, though the Ledger API marks the field as required.");
        error.SourceException.Should().BeOfType<MalformedResponseException>();
    }
}
