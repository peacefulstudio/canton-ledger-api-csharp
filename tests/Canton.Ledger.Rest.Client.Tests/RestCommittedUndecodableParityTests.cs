// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using Canton.Ledger.Testing.Helpers;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Microsoft.Extensions.Options;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestCommittedUndecodableParityTests : CommittedUndecodableParityTests, IDisposable
{
    private readonly List<StubHttpClientFactory> _factories = [];

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    protected override Task<ExerciseOutcome<TransactionResult>> SubmitTransactionCarryingAsync(
        UndecodableWireShape shape) =>
        SubmitAsync($$"""{"transaction": {{TransactionCarrying(shape)}}}""");

    protected override Task<ExerciseOutcome<TransactionResult>> SubmitWithoutTransactionAsync() =>
        SubmitAsync("{}");

    private Task<ExerciseOutcome<TransactionResult>> SubmitAsync(string responseBody)
    {
        var factory = new StubHttpClientFactory(
            new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, responseBody));
        _factories.Add(factory);
        var client = new RestLedgerClient(
            factory, Options.Create(new RestLedgerClientOptions { HttpAddress = "http://localhost:7575" }));
        var create = new RuntimeCommands.CreateCommand(
            new RuntimeIdentifier("pkg", "Module", "Entity"), new DamlRecord(null, []));
        var submission = RuntimeCommands.CommandsSubmission.Single(create).WithActAs((Party)"party::alice");

        return client.TrySubmitAndWaitForTransactionAsync(
            submission, cancellationToken: TestContext.Current.CancellationToken);
    }

    private static string TransactionCarrying(UndecodableWireShape shape)
    {
        var commandId = shape == UndecodableWireShape.WhitespaceCommandId ? "   " : "cmd-1";
        var offset = shape == UndecodableWireShape.NegativeOffset ? "-1" : "42";
        var events = shape == UndecodableWireShape.EmptyActingParty
            ? """
              [{"ExercisedEvent": {
                "nodeId": 0, "contractId": "00aa",
                "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "Entity"},
                "choice": "Accept", "choiceArgument": {}, "exerciseResult": {},
                "actingParties": [""], "lastDescendantNodeId": 0}}]
              """
            : "[]";

        return $$"""
            {"updateId": "{{DeclaredUpdateId}}", "commandId": "{{commandId}}", "offset": "{{offset}}", "events": {{events}}}
            """;
    }
}
