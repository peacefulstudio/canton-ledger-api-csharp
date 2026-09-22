// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Testing.Helpers;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using WireCreatedEvent = Canton.Ledger.Rest.Client.Raw.CreatedEvent;
using WireEvent = Canton.Ledger.Rest.Client.Raw.Event;
using WireIdentifier = Canton.Ledger.Rest.Client.Raw.Identifier;
using WireInterfaceView = Canton.Ledger.Rest.Client.Raw.InterfaceView;
using WireRecord = Canton.Ledger.Rest.Client.Raw.Record;
using WireStatus = Canton.Ledger.Rest.Client.Raw.Status;
using WireTransaction = Canton.Ledger.Rest.Client.Raw.Transaction;
using WireValue = Canton.Ledger.Rest.Client.Raw.Value;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestContractKeyHashParityTests : ContractKeyHashParityTests
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
        RestContractStreamProjector.ProjectTransactionEvents<TemplateMarker>(KeyedTransaction())
            .Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>().Subject.Key;

    private static ContractKey? InterfaceStreamKey() =>
        RestInterfaceStreamProjector.ProjectTransactionEvents<InterfaceMarker, InterfaceMarkerView>(KeyedTransaction())
            .Should().ContainSingle().Subject
            .Should().BeOfType<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Created>().Subject.Key;

    private static ContractKey? TransactionResultKey() =>
        RestTransactionResultProjector.Project(KeyedTransaction())
            .CreatedContracts.Should().ContainSingle().Subject.ContractKey;

    private static ContractKey? TransactionTreeKey() =>
        RestTransactionTreeProjector.Project(KeyedTransaction())
            .RootEvents.Should().ContainSingle().Subject
            .Should().BeOfType<TreeEvent.Created>().Subject.ContractKey;

    private static WireTransaction KeyedTransaction() => new()
    {
        UpdateId = "update-1",
        CommandId = "cmd-1",
        Offset = "42",
        SynchronizerId = "sync-1",
        Events = [new WireEvent { CreatedEvent = KeyedCreatedEvent() }],
    };

    private static WireCreatedEvent KeyedCreatedEvent() => new()
    {
        NodeId = 0,
        Offset = "42",
        ContractId = "00keyed",
        TemplateId = TemplateId,
        CreateArgument = OwnerRecord(),
        ContractKey = FromDamlLfJson<WireValue>("\"alice::ns1\""),
        ContractKeyHash = ProjectedKeyHash,
        InterfaceViews = [ComputedInterfaceView()],
        WitnessParties = [KeyParty],
    };

    private static WireInterfaceView ComputedInterfaceView() => new()
    {
        InterfaceId = InterfaceId,
        ViewStatus = new WireStatus { Code = 0 },
        ViewValue = AmountRecord(),
    };

    private static WireRecord OwnerRecord() => FromDamlLfJson<WireRecord>("""{"owner": "alice::ns1"}""");

    private static WireRecord AmountRecord() => FromDamlLfJson<WireRecord>("""{"amount": "view-value"}""");

    private static T FromDamlLfJson<T>(string lfJson) =>
        JsonSerializer.Deserialize<T>(lfJson, RestRefitSettings.SerializerOptions)!;

    private static WireIdentifier TemplateId => new()
    {
        PackageId = TemplateMarker.TemplateId.PackageId,
        ModuleName = TemplateMarker.TemplateId.ModuleName,
        EntityName = TemplateMarker.TemplateId.EntityName,
    };

    private static WireIdentifier InterfaceId => new()
    {
        PackageId = InterfaceMarker.InterfaceId.PackageId,
        ModuleName = InterfaceMarker.InterfaceId.ModuleName,
        EntityName = InterfaceMarker.InterfaceId.EntityName,
    };
}
