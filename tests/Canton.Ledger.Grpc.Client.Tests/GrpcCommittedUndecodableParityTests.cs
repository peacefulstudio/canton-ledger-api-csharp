// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Testing.Helpers;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Grpc.Core;
using Xunit;
using Grpc.Net.Client;
using NSubstitute;
using ProtoExercisedEvent = Com.Daml.Ledger.Api.V2.ExercisedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class GrpcCommittedUndecodableParityTests : CommittedUndecodableParityTests, IDisposable
{
    private readonly LedgerClientOptions _options = new()
    {
        GrpcAddress = "https://localhost:5001",
        UserId = "test-user",
    };

    private readonly GrpcChannel _channel;
    private readonly CommandService.CommandServiceClient _commandService;

    public GrpcCommittedUndecodableParityTests()
    {
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);
        _commandService = Substitute.ForPartsOf<CommandService.CommandServiceClient>(Substitute.For<CallInvoker>());
    }

    public void Dispose() => _channel.Dispose();

    protected override Task<ExerciseOutcome<TransactionResult>> SubmitTransactionCarryingAsync(
        UndecodableWireShape shape) =>
        SubmitAsync(new SubmitAndWaitForTransactionResponse { Transaction = TransactionCarrying(shape) });

    protected override Task<ExerciseOutcome<TransactionResult>> SubmitWithoutTransactionAsync() =>
        SubmitAsync(new SubmitAndWaitForTransactionResponse());

    private Task<ExerciseOutcome<TransactionResult>> SubmitAsync(SubmitAndWaitForTransactionResponse response)
    {
        LedgerClientTestFixtures.StubCommandServiceSuccess(_commandService, response);
        var client = new LedgerClient(_options, _channel, _commandService, new StaticTokenProvider("test-token"));
        var create = new RuntimeCommands.CreateCommand(
            new RuntimeIdentifier("pkg", "Module", "Entity"), new DamlRecord(null, []));
        var submission = RuntimeCommands.CommandsSubmission.Single(create).WithActAs((Party)"party::alice");

        return client.TrySubmitAndWaitForTransactionAsync(
            submission, cancellationToken: TestContext.Current.CancellationToken);
    }

    private static Transaction TransactionCarrying(UndecodableWireShape shape)
    {
        var transaction = new Transaction
        {
            UpdateId = DeclaredUpdateId,
            CommandId = shape == UndecodableWireShape.WhitespaceCommandId ? "   " : "cmd-1",
            Offset = shape == UndecodableWireShape.NegativeOffset ? -1L : 42L,
        };

        if (shape == UndecodableWireShape.EmptyActingParty)
        {
            var exercised = new ProtoExercisedEvent
            {
                NodeId = 0,
                ContractId = "00aa",
                TemplateId = new ProtoIdentifier { PackageId = "pkg", ModuleName = "Module", EntityName = "Entity" },
                Choice = "Accept",
                ChoiceArgument = new Com.Daml.Ledger.Api.V2.Value { Unit = new Google.Protobuf.WellKnownTypes.Empty() },
                ExerciseResult = new Com.Daml.Ledger.Api.V2.Value { Unit = new Google.Protobuf.WellKnownTypes.Empty() },
                LastDescendantNodeId = 0,
            };
            exercised.ActingParties.Add(string.Empty);
            transaction.Events.Add(new Event { Exercised = exercised });
        }

        return transaction;
    }
}
