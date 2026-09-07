// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Testing.Helpers;
using Xunit;
using WireEvent = Canton.Ledger.Rest.Client.Raw.Event;
using WireExercisedEvent = Canton.Ledger.Rest.Client.Raw.ExercisedEvent;
using WireGetUpdateResponse = Canton.Ledger.Rest.Client.Raw.GetUpdateResponse;
using WireIdentifier = Canton.Ledger.Rest.Client.Raw.Identifier;
using WireTransaction = Canton.Ledger.Rest.Client.Raw.Transaction;
using WireUpdate = Canton.Ledger.Rest.Client.Raw.Update;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestPointReadDecodeParityTests : PointReadDecodeParityTests
{
    private static readonly WireIdentifier TemplateId = new()
    {
        PackageId = "pkg",
        ModuleName = "Module",
        EntityName = "Entity",
    };

    protected override Exception EscapingPointRead(Exception decodeFailure)
    {
        var response = new WireGetUpdateResponse
        {
            Update = new WireUpdate
            {
                Transaction = new WireTransaction { UpdateId = "update-1", Offset = "42" },
            },
        };

        return Record.Exception(() => RestLedgerClient.ProjectPointRead<int>(
            response, LookupDescription, _ => throw decodeFailure))!;
    }

    protected override Exception EscapingPointReadOf(UndecodableWireShape shape)
    {
        var response = new WireGetUpdateResponse
        {
            Update = new WireUpdate { Transaction = TransactionCarrying(shape) },
        };

        return Record.Exception(() => RestLedgerClient.ProjectPointRead(
            response, LookupDescription, RestTransactionResultProjector.Project))!;
    }

    private static WireTransaction TransactionCarrying(UndecodableWireShape shape) => new()
    {
        UpdateId = "update-1",
        CommandId = shape == UndecodableWireShape.WhitespaceCommandId ? "   " : "cmd-1",
        Offset = shape == UndecodableWireShape.NegativeOffset ? "-1" : "42",
        Events = shape == UndecodableWireShape.EmptyActingParty
            ?
            [
                new WireEvent
                {
                    ExercisedEvent = new WireExercisedEvent
                    {
                        NodeId = 0,
                        ContractId = "00aa",
                        TemplateId = TemplateId,
                        Choice = "Accept",
                        LastDescendantNodeId = 0,
                        ActingParties = [string.Empty],
                    },
                },
            ]
            : [],
    };
}
