// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Testing.Helpers;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Contracts;
using Daml.Runtime.Streams;
using Google.Protobuf;
using Google.Rpc;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using ProtoRecord = Com.Daml.Ledger.Api.V2.Record;
using ProtoValue = Com.Daml.Ledger.Api.V2.Value;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class GrpcContractKeyHashParityTests : ContractKeyHashParityTests
{
    protected override ContractKey? ProjectedKeyOn(ContractKeyProjectionPath path) => path switch
    {
        ContractKeyProjectionPath.ContractStream => ContractStreamKey(),
        ContractKeyProjectionPath.InterfaceStream => InterfaceStreamKey(),
        ContractKeyProjectionPath.TransactionResult => TransactionResultKey(),
        ContractKeyProjectionPath.TransactionTree => TransactionTreeKey(),
        _ => throw new ArgumentOutOfRangeException(nameof(path)),
    };

    private static ContractKey? ContractStreamKey() =>
        ContractStreamProjector.ProjectTransactionEvents<TemplateMarker>(KeyedTransaction())
            .Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>().Subject.Key;

    private static ContractKey? InterfaceStreamKey() =>
        InterfaceStreamProjector.ProjectTransactionEvents<InterfaceMarker, InterfaceMarkerView>(KeyedTransaction())
            .Should().ContainSingle().Subject
            .Should().BeOfType<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Created>().Subject.Key;

    private static ContractKey? TransactionResultKey() =>
        GrpcTransactionResultProjector.Project(KeyedTransaction())
            .CreatedContracts.Should().ContainSingle().Subject.ContractKey;

    private static ContractKey? TransactionTreeKey() =>
        GrpcTransactionTreeProjector.Project(KeyedTransaction())
            .RootEvents.Should().ContainSingle().Subject
            .Should().BeOfType<TreeEvent.Created>().Subject.ContractKey;

    private static Transaction KeyedTransaction()
    {
        var transaction = new Transaction
        {
            UpdateId = "update-1",
            CommandId = "cmd-1",
            Offset = 42L,
            SynchronizerId = "sync-1",
        };
        transaction.Events.Add(new Event { Created = KeyedCreatedEvent() });
        return transaction;
    }

    private static ProtoCreatedEvent KeyedCreatedEvent()
    {
        var created = new ProtoCreatedEvent
        {
            NodeId = 0,
            ContractId = "00keyed",
            TemplateId = TemplateId,
            CreateArguments = OwnerRecord(),
            ContractKey = new ProtoValue { Party = KeyParty },
            ContractKeyHash = ByteString.CopyFrom(KeyHashBytes),
            Offset = 42L,
        };
        created.InterfaceViews.Add(ComputedInterfaceView());
        created.WitnessParties.Add(KeyParty);
        return created;
    }

    private static InterfaceView ComputedInterfaceView() => new()
    {
        InterfaceId = InterfaceId,
        ViewStatus = new Status { Code = 0 },
        ViewValue = AmountRecord(),
    };

    private static ProtoRecord OwnerRecord() => new()
    {
        Fields = { new RecordField { Label = "owner", Value = new ProtoValue { Party = KeyParty } } },
    };

    private static ProtoRecord AmountRecord() => new()
    {
        Fields = { new RecordField { Label = "amount", Value = new ProtoValue { Text = "view-value" } } },
    };

    private static ProtoIdentifier TemplateId => new()
    {
        PackageId = TemplateMarker.TemplateId.PackageId,
        ModuleName = TemplateMarker.TemplateId.ModuleName,
        EntityName = TemplateMarker.TemplateId.EntityName,
    };

    private static ProtoIdentifier InterfaceId => new()
    {
        PackageId = InterfaceMarker.InterfaceId.PackageId,
        ModuleName = InterfaceMarker.InterfaceId.ModuleName,
        EntityName = InterfaceMarker.InterfaceId.EntityName,
    };
}
