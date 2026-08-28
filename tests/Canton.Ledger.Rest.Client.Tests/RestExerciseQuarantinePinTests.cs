// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

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
/// Pins the interim behavior of the quarantined REST exercise path: the bytes it submits,
/// which no live test covers while the quarantine holds; a transaction carrying an
/// ArchivedEvent and no ExercisedEvent, which the participant returned under the ACS-delta
/// default the client no longer requests and which must still fail loudly if it ever
/// arrives; and the ledger-effects transaction measured live, whose choiceArgument and
/// exerciseResult arrive as an untyped empty wire Value. The quarantined suites are
/// <c>RestLedgerWriterConformanceTests</c> and <c>RestLedgerWriterParityTests</c>; when the
/// decode fix lands and either response pin breaks, lift the quarantine and close the
/// tracking issue.
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

    private sealed record TestTemplate : ITemplate
    {
        public static RuntimeIdentifier TemplateId { get; } = new("pkg", "Module", "Template");
        public static string PackageId => "pkg";
        public static string PackageName => "pkg-name";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => new(TemplateId, []);
    }

    private RestLedgerClient Client() => new(_factory, Options.Create(new RestLedgerClientOptions
    {
        HttpAddress = "http://localhost:7575",
    }));

    private static ExerciseCommand ArchiveCommand() => ExerciseCommand.For(
        new ContractId<TestTemplate>("00marker"), new ChoiceName("Archive"), DamlRecord.Create());

    private const string UntypedExerciseResultTransaction =
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
                  "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "Template"},
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
    public async Task TryExerciseAsync_requests_the_ledger_effects_shape_and_throws_when_the_transaction_carries_no_ExercisedEvent()
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

        var decodeFailure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.TryExerciseAsync<DamlUnit>(
                ArchiveCommand(), Alice, cancellationToken: TestContext.Current.CancellationToken));

        decodeFailure.Message.Should().Be("Transaction contains no exercised event for choice 'Archive'.");
        SubmittedTransactionShape().Should().Be("TRANSACTION_SHAPE_LEDGER_EFFECTS",
            "the exercise path asks for ledger effects so the ExercisedEvent is present at all; "
            + "the remaining quarantine cause is the untyped payload decode");
    }

    [Fact]
    public async Task TryExerciseAsync_returns_InfraError_when_the_ledger_effects_exerciseResult_arrives_as_an_untyped_empty_Value()
    {
        _transport.WithResponse(HttpStatusCode.OK, UntypedExerciseResultTransaction);
        var client = Client();

        var outcome = await client.TryExerciseAsync<DamlUnit>(
            ArchiveCommand(), Alice, cancellationToken: TestContext.Current.CancellationToken);

        var infraError = outcome.Should().BeOfType<ExerciseOutcome<DamlUnit>.InfraError>().Subject;
        infraError.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
        infraError.Message.Should().Be(
            "Server returned a malformed transaction: Malformed response from ledger: "
            + "Received a wire Value with no recognisable sum case set.");
    }

    [Fact]
    public async Task TryExerciseAsync_submits_the_choice_against_the_contract_the_command_names()
    {
        _transport.WithResponse(HttpStatusCode.OK, UntypedExerciseResultTransaction);
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
        exercise.GetProperty("templateId").GetString().Should().Be("pkg:Module:Template");
        exercise.GetProperty("contractId").GetString().Should().Be("00marker");
        exercise.GetProperty("choice").GetString().Should().Be("Archive");
        exercise.GetProperty("choiceArgument").GetRawText().Should().Be("{}",
            "the request side already writes LF-JSON, so an empty choice argument goes out in the "
            + "same untyped shape the response side cannot read back");
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
