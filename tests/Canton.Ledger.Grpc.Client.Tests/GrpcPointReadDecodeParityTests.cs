// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Testing.Helpers;
using Com.Daml.Ledger.Api.V2;
using Xunit;
using ProtoExercisedEvent = Com.Daml.Ledger.Api.V2.ExercisedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using XunitRecord = Xunit.Record;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class GrpcPointReadDecodeParityTests : PointReadDecodeParityTests
{
    private static readonly ProtoIdentifier TemplateId = new()
    {
        PackageId = "pkg",
        ModuleName = "Module",
        EntityName = "Entity",
    };

    protected override Exception EscapingPointRead(Exception decodeFailure)
    {
        var response = new GetUpdateResponse
        {
            Transaction = new Transaction { UpdateId = "update-1", Offset = 42L, CommandId = "cmd-1" },
        };

        return XunitRecord.Exception(() => LedgerClient.ProjectPointRead<int>(
            response, LookupDescription, _ => throw decodeFailure))!;
    }

    protected override Exception EscapingPointReadOf(UndecodableWireShape shape)
    {
        var response = new GetUpdateResponse { Transaction = TransactionCarrying(shape) };

        return XunitRecord.Exception(() => LedgerClient.ProjectPointRead(
            response, LookupDescription, GrpcTransactionResultProjector.Project))!;
    }

    private static Transaction TransactionCarrying(UndecodableWireShape shape)
    {
        var transaction = new Transaction
        {
            UpdateId = "update-1",
            CommandId = shape == UndecodableWireShape.WhitespaceCommandId ? "   " : "cmd-1",
            Offset = shape == UndecodableWireShape.NegativeOffset ? -1L : 42L,
        };

        if (shape == UndecodableWireShape.EmptyActingParty)
        {
            var exercised = new ProtoExercisedEvent
            {
                NodeId = 0,
                ContractId = "00aa",
                TemplateId = TemplateId,
                Choice = "Accept",
                LastDescendantNodeId = 0,
            };
            exercised.ActingParties.Add(string.Empty);
            transaction.Events.Add(new Event { Exercised = exercised });
        }

        return transaction;
    }
}
