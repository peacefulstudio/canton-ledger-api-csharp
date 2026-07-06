// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Com.Daml.Ledger.Api.V2;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using AwesomeAssertions;
using Google.Rpc;
using Xunit;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using ProtoRecord = Com.Daml.Ledger.Api.V2.Record;
using ProtoValue = Com.Daml.Ledger.Api.V2.Value;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Grpc.Client.Tests;

public class ContractStreamProjectorTests
{
    private static readonly ProtoIdentifier MatchingInterfaceId =
        new() { PackageId = "iface-pkg", ModuleName = "Token.Api", EntityName = "IHolding" };

    [Fact]
    public void ProjectTransactionEvents_decodes_Created_Payload_from_matching_interface_view_for_interface_marker()
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00holding",
            TemplateId = new ProtoIdentifier { PackageId = "impl-pkg", ModuleName = "Token.Holding", EntityName = "Holding" },
            CreateArguments = new ProtoRecord
            {
                Fields = { new RecordField { Label = "amount", Value = new ProtoValue { Text = "create-arguments-value" } } },
            },
            Offset = 10L,
        };
        created.InterfaceViews.Add(new InterfaceView
        {
            InterfaceId = MatchingInterfaceId,
            ViewStatus = new Status { Code = 0 },
            ViewValue = new ProtoRecord
            {
                Fields = { new RecordField { Label = "amount", Value = new ProtoValue { Text = "view-value" } } },
            },
        });
        var transaction = new Transaction { SynchronizerId = "sync-1" };
        transaction.Events.Add(new Event { Created = created });

        var events = ContractStreamProjector.ProjectTransactionEvents<InterfaceMarker>(transaction).ToList();

        var typed = events.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<InterfaceMarker>.Created>().Subject;
        typed.ContractId.Value.Should().Be("00holding");
        typed.Payload.GetRequiredField("amount").As<DamlText>().Value.Should().Be("view-value");
    }

    [Theory]
    [MemberData(nameof(UndecodableInterfaceViews))]
    public void ProjectTransactionEvents_yields_Unclassified_when_interface_view_is_undecodable(InterfaceView undecodableView)
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00holding",
            TemplateId = new ProtoIdentifier { PackageId = "impl-pkg", ModuleName = "Token.Holding", EntityName = "Holding" },
            CreateArguments = new ProtoRecord(),
            Offset = 11L,
        };
        created.InterfaceViews.Add(undecodableView);
        var transaction = new Transaction { SynchronizerId = "sync-1" };
        transaction.Events.Add(new Event { Created = created });

        var events = ContractStreamProjector.ProjectTransactionEvents<InterfaceMarker>(transaction).ToList();

        var unclassified = events.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<InterfaceMarker>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(11L);
        unclassified.Kind.Should().Be(ContractStreamProjector.UnclassifiedKind.InterfaceViewUnavailable);
    }

    public static IEnumerable<object[]> UndecodableInterfaceViews()
    {
        yield return
        [
            new InterfaceView
            {
                InterfaceId = MatchingInterfaceId,
                ViewStatus = new Status { Code = 2, Message = "view computation failed" },
            },
        ];
        yield return
        [
            new InterfaceView
            {
                InterfaceId = MatchingInterfaceId,
                ViewStatus = new Status { Code = 0 },
            },
        ];
    }

    [Fact]
    public void ProjectActiveContractEntry_decodes_Payload_from_matching_interface_view()
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00holding",
            TemplateId = new ProtoIdentifier { PackageId = "impl-pkg", ModuleName = "Token.Holding", EntityName = "Holding" },
            CreateArguments = new ProtoRecord(),
            Offset = 20L,
        };
        created.InterfaceViews.Add(new InterfaceView
        {
            InterfaceId = MatchingInterfaceId,
            ViewStatus = new Status { Code = 0 },
            ViewValue = new ProtoRecord
            {
                Fields = { new RecordField { Label = "amount", Value = new ProtoValue { Text = "acs-view-value" } } },
            },
        });
        var response = new GetActiveContractsResponse
        {
            ActiveContract = new ActiveContract
            {
                CreatedEvent = created,
                SynchronizerId = "sync-1",
            },
        };

        var projected = ContractStreamProjector.ProjectActiveContractEntry<InterfaceMarker>(response);

        var typed = projected.Should().BeOfType<ContractStreamEvent<InterfaceMarker>.Created>().Subject;
        typed.Payload.GetRequiredField("amount").As<DamlText>().Value.Should().Be("acs-view-value");
    }

    [Theory]
    [MemberData(nameof(UndecodableInterfaceViews))]
    public void ProjectActiveContractEntry_yields_Unclassified_when_interface_view_is_undecodable(InterfaceView undecodableView)
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00holding",
            TemplateId = new ProtoIdentifier { PackageId = "impl-pkg", ModuleName = "Token.Holding", EntityName = "Holding" },
            CreateArguments = new ProtoRecord(),
            Offset = 21L,
        };
        created.InterfaceViews.Add(undecodableView);
        var response = new GetActiveContractsResponse
        {
            ActiveContract = new ActiveContract
            {
                CreatedEvent = created,
                SynchronizerId = "sync-1",
            },
        };

        var projected = ContractStreamProjector.ProjectActiveContractEntry<InterfaceMarker>(response);

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<InterfaceMarker>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(21L);
        unclassified.Kind.Should().Be(ContractStreamProjector.UnclassifiedKind.InterfaceViewUnavailable);
    }

    [Fact]
    public void ProjectReassignmentEvents_Assigned_decodes_Payload_from_matching_interface_view()
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00holding",
            TemplateId = new ProtoIdentifier { PackageId = "impl-pkg", ModuleName = "Token.Holding", EntityName = "Holding" },
            CreateArguments = new ProtoRecord(),
            Offset = 30L,
        };
        created.InterfaceViews.Add(new InterfaceView
        {
            InterfaceId = MatchingInterfaceId,
            ViewStatus = new Status { Code = 0 },
            ViewValue = new ProtoRecord
            {
                Fields = { new RecordField { Label = "amount", Value = new ProtoValue { Text = "assigned-view-value" } } },
            },
        });
        var reassignment = new Reassignment { Offset = 30L };
        reassignment.Events.Add(new ReassignmentEvent
        {
            Assigned = new AssignedEvent
            {
                Source = "sync-src",
                Target = "sync-tgt",
                CreatedEvent = created,
            },
        });

        var events = ContractStreamProjector.ProjectReassignmentEvents<InterfaceMarker>(reassignment).ToList();

        var typed = events.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<InterfaceMarker>.Assigned>().Subject;
        typed.Payload.GetRequiredField("amount").As<DamlText>().Value.Should().Be("assigned-view-value");
    }

    [Theory]
    [MemberData(nameof(UndecodableInterfaceViews))]
    public void ProjectReassignmentEvents_Assigned_yields_Unclassified_when_interface_view_is_undecodable(InterfaceView undecodableView)
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00holding",
            TemplateId = new ProtoIdentifier { PackageId = "impl-pkg", ModuleName = "Token.Holding", EntityName = "Holding" },
            CreateArguments = new ProtoRecord(),
            Offset = 31L,
        };
        created.InterfaceViews.Add(undecodableView);
        var reassignment = new Reassignment { Offset = 31L };
        reassignment.Events.Add(new ReassignmentEvent
        {
            Assigned = new AssignedEvent
            {
                Source = "sync-src",
                Target = "sync-tgt",
                CreatedEvent = created,
            },
        });

        var events = ContractStreamProjector.ProjectReassignmentEvents<InterfaceMarker>(reassignment).ToList();

        var unclassified = events.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<InterfaceMarker>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(31L);
        unclassified.Kind.Should().Be(ContractStreamProjector.UnclassifiedKind.InterfaceViewUnavailable);
    }

    [Fact]
    public void ProjectTransactionEvents_Created_Payload_still_sourced_from_CreateArguments_for_template_marker()
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00tmpl",
            TemplateId = new ProtoIdentifier { PackageId = "tmpl-pkg", ModuleName = "Sample.Token", EntityName = "Holding" },
            CreateArguments = new ProtoRecord
            {
                Fields = { new RecordField { Label = "owner", Value = new ProtoValue { Party = "alice" } } },
            },
            Offset = 40L,
        };
        var transaction = new Transaction { SynchronizerId = "sync-1" };
        transaction.Events.Add(new Event { Created = created });

        var events = ContractStreamProjector.ProjectTransactionEvents<TemplateMarker>(transaction).ToList();

        var typed = events.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>().Subject;
        typed.Payload.GetRequiredField("owner").As<DamlParty>().Value.Should().Be("alice");
    }

    internal sealed record InterfaceMarker : IDamlInterface
    {
        public static RuntimeIdentifier InterfaceId { get; } = new("iface-pkg", "Token.Api", "IHolding");
        public static string PackageId => "iface-pkg";
        public static string PackageName => "token-api";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(InterfaceId, DamlTypeKind.Interface, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create();
    }

    internal sealed record TemplateMarker(string Owner) : ITemplate
    {
        public static RuntimeIdentifier TemplateId { get; } = new("tmpl-pkg", "Sample.Token", "Holding");
        public static string PackageId => "tmpl-pkg";
        public static string PackageName => "token-impl";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("owner", new DamlParty(Owner)));
    }
}
