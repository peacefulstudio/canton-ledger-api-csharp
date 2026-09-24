// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Serialization;
using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Microsoft.Extensions.Options;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Rest.Client.Tests;

/// <summary>
/// Pins the REST exercise path's behavior on two shapes a live participant is known to send:
/// the bytes it submits; a transaction carrying an ArchivedEvent and no ExercisedEvent, which
/// the participant returned under the ACS-delta default the client no longer requests and
/// which must still surface as committed but undecodable if it ever arrives; and a ledger-effects transaction whose
/// choiceArgument and exerciseResult arrive as <c>{}</c>, the Daml-LF JSON encoding of Unit,
/// decoded against the choice's declared Unit types.
/// </summary>
public sealed class RestExerciseQuarantinePinTests : IDisposable
{
    private static readonly Party Alice = new("party::alice");

    private readonly RecordingHttpHandler _transport = new();
    private readonly StubHttpClientFactory _factory;

    public RestExerciseQuarantinePinTests()
    {
        _factory = new StubHttpClientFactory(_transport);
    }

    public void Dispose() => _factory.Dispose();

    private sealed record TestTemplate : ITemplate, IDamlRecord<TestTemplate>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("pkg", "Module", "QuarantinePinTemplate");
        public static string PackageId => "pkg";
        public static string PackageName => "pkg-name";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => new(TemplateId, []);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(json, context);
        public static TestTemplate FromRecord(DamlRecord record) =>
            new();

        public static Choice<TestTemplate, DamlUnit, DamlUnit> ChoiceArchive { get; } = new()
        {
            Name = new ChoiceName("Archive"),
            Consuming = true,
            ArgumentEncoder = unit => unit,
            ResultDecoder = result => result.As<DamlUnit>(),
            ArgumentDecoder = value => value.As<DamlUnit>(),
            ArgumentJsonReader = DamlLfJsonDecoders.ReadUnit,
            ResultJsonReader = DamlLfJsonDecoders.ReadUnit,
        };
    }

    private RestLedgerClient Client() => new(_factory, Options.Create(new RestLedgerClientOptions
    {
        HttpAddress = "http://localhost:7575",
    }));

    private static ExerciseCommand ArchiveCommand() => ExerciseCommand.For(
        new ContractId<TestTemplate>("00marker"), new ChoiceName("Archive"), DamlRecord.Create());

    private const string UnitExerciseResultTransaction =
        """
        {
          "transaction": {
            "updateId": "upd-1",
            "commandId": "cmd-1",
            "offset": "7",
            "events": [
              {
                "ExercisedEvent": {
                  "offset": "7",
                  "contractId": "00marker",
                  "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "QuarantinePinTemplate"},
                  "choice": "Archive",
                  "choiceArgument": {},
                  "actingParties": ["party::alice"],
                  "consuming": true,
                  "witnessParties": ["party::alice"],
                  "exerciseResult": {}
                }
              }
            ]
          }
        }
        """;

    [Fact]
    public async Task TryExerciseAsync_requests_the_ledger_effects_shape_and_reports_CommittedUndecodable_when_the_transaction_carries_no_ExercisedEvent()
    {
        _transport.WithResponse(
            HttpStatusCode.OK,
            """
            {
              "transaction": {
                "updateId": "upd-1",
                "commandId": "cmd-1",
                "offset": "7",
                "events": [{"ArchivedEvent": {"offset": "7", "contractId": "00marker"}}]
              }
            }
            """);
        var client = Client();

        var outcome = await client.TryExerciseAsync<DamlUnit>(
            ArchiveCommand(), Alice, cancellationToken: TestContext.Current.CancellationToken);

        var undecodable = outcome.Should().BeOfType<ExerciseOutcome<DamlUnit>.CommittedUndecodable>().Subject;
        undecodable.UpdateId.Should().Be("upd-1");
        undecodable.Message.Should().Be(
            "The command committed, but its choice result could not be read: Transaction contains no exercised event for choice 'Archive'.");
        undecodable.SourceException.Should().BeOfType<InvalidOperationException>();
        SubmittedTransactionShape().Should().Be("TRANSACTION_SHAPE_LEDGER_EFFECTS",
            "the exercise path asks for ledger effects so the ExercisedEvent is present at all");
    }

    [Fact]
    public async Task TryExerciseAsync_returns_One_with_Unit_when_the_ledger_effects_exerciseResult_arrives_as_an_empty_object()
    {
        _transport.WithResponse(HttpStatusCode.OK, UnitExerciseResultTransaction);
        var client = Client();

        var outcome = await client.TryExerciseAsync<DamlUnit>(
            ArchiveCommand(), Alice, cancellationToken: TestContext.Current.CancellationToken);

        var one = outcome.Should().BeOfType<ExerciseOutcome<DamlUnit>.One>().Subject;
        one.Result.Should().Be(DamlUnit.Instance);
    }

    [Fact]
    public async Task TryExerciseAsync_submits_the_choice_against_the_contract_the_command_names()
    {
        _transport.WithResponse(HttpStatusCode.OK, UnitExerciseResultTransaction);
        var client = Client();

        await client.TryExerciseAsync<DamlUnit>(
            ArchiveCommand(), Alice, cancellationToken: TestContext.Current.CancellationToken);

        using var submitted = JsonDocument.Parse(_transport.LastRequestBody!);
        var envelope = submitted.RootElement.GetProperty("commands");
        envelope.GetProperty("actAs").EnumerateArray().Select(actor => actor.GetString())
            .Should().Equal(Alice.Id);

        var command = envelope.GetProperty("commands").EnumerateArray().Should().ContainSingle().Subject;
        command.EnumerateObject().Select(arm => arm.Name).Should().Equal("ExerciseCommand");

        var exercise = command.GetProperty("ExerciseCommand");
        exercise.GetProperty("templateId").GetString().Should().Be("pkg:Module:QuarantinePinTemplate");
        exercise.GetProperty("contractId").GetString().Should().Be("00marker");
        exercise.GetProperty("choice").GetString().Should().Be("Archive");
        exercise.GetProperty("choiceArgument").GetRawText().Should().Be("{}",
            "the request side writes Daml-LF JSON, so a Unit choice argument goes out as an empty object");
    }

    private string? SubmittedTransactionShape()
    {
        using var submitted = JsonDocument.Parse(_transport.LastRequestBody!);
        return submitted.RootElement.TryGetProperty("transactionFormat", out var format)
            && format.TryGetProperty("transactionShape", out var shape)
                ? shape.GetString()
                : null;
    }
}
