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
using ProtoExercisedEvent = Com.Daml.Ledger.Api.V2.ExercisedEvent;
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
        unclassified.Offset.Value.Should().Be(11L);
        unclassified.Kind.Should().Be(UnclassifiedKind.InterfaceViewUnavailable);
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

        var projected = ContractStreamProjector.ProjectActiveContractEntry<InterfaceMarker>(response).ToList();

        var typed = projected.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<InterfaceMarker>.Created>().Subject;
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

        var projected = ContractStreamProjector.ProjectActiveContractEntry<InterfaceMarker>(response).ToList();

        var unclassified = projected.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<InterfaceMarker>.Unclassified>().Subject;
        unclassified.Offset.Value.Should().Be(21L);
        unclassified.Kind.Should().Be(UnclassifiedKind.InterfaceViewUnavailable);
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
        unclassified.Offset.Value.Should().Be(31L);
        unclassified.Kind.Should().Be(UnclassifiedKind.InterfaceViewUnavailable);
    }

    private static ProtoValue UnsetSumValue() => new();

    public static TheoryData<ProtoValue> PoisonValues =>
        new(LedgerClientTestFixtures.OutOfDecimalRangeNumeric(), UnsetSumValue());

    [Theory]
    [MemberData(nameof(PoisonValues))]
    public void ProjectTransactionEvents_surfaces_Created_with_undecodable_create_arguments_as_decode_failure_Unclassified(ProtoValue poisonValue)
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00poison",
            TemplateId = new ProtoIdentifier { PackageId = "tmpl-pkg", ModuleName = "Sample.Token", EntityName = "Holding" },
            CreateArguments = new ProtoRecord
            {
                Fields = { new RecordField { Label = "amount", Value = poisonValue } },
            },
            Offset = 50L,
        };
        var transaction = new Transaction { SynchronizerId = "sync-1" };
        transaction.Events.Add(new Event { Created = created });

        var events = ContractStreamProjector.ProjectTransactionEvents<TemplateMarker>(transaction).ToList();

        var unclassified = events.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Value.Should().Be(50L);
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
    }

    [Theory]
    [MemberData(nameof(PoisonValues))]
    public void ProjectTransactionEvents_surfaces_Exercised_with_undecodable_choice_argument_as_decode_failure_Unclassified(ProtoValue poisonValue)
    {
        var exercised = new ProtoExercisedEvent
        {
            ContractId = "00poison",
            TemplateId = new ProtoIdentifier { PackageId = "tmpl-pkg", ModuleName = "Sample.Token", EntityName = "Holding" },
            Choice = "Accept",
            ChoiceArgument = poisonValue,
            ExerciseResult = new ProtoValue { Unit = new Google.Protobuf.WellKnownTypes.Empty() },
            Consuming = true,
            Offset = 51L,
        };
        var transaction = new Transaction { SynchronizerId = "sync-1" };
        transaction.Events.Add(new Event { Exercised = exercised });

        var events = ContractStreamProjector.ProjectTransactionEvents<TemplateMarker>(transaction).ToList();

        var unclassified = events.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Value.Should().Be(51L);
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
    }

    [Theory]
    [MemberData(nameof(PoisonValues))]
    public void ProjectReassignmentEvents_surfaces_Assigned_with_undecodable_payload_as_decode_failure_Unclassified(ProtoValue poisonValue)
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00poison",
            TemplateId = new ProtoIdentifier { PackageId = "tmpl-pkg", ModuleName = "Sample.Token", EntityName = "Holding" },
            CreateArguments = new ProtoRecord
            {
                Fields = { new RecordField { Label = "amount", Value = poisonValue } },
            },
            Offset = 60L,
        };
        var reassignment = new Reassignment { Offset = 99L };
        reassignment.Events.Add(new ReassignmentEvent
        {
            Assigned = new AssignedEvent
            {
                Source = "sync-src",
                Target = "sync-tgt",
                CreatedEvent = created,
            },
        });

        var events = ContractStreamProjector.ProjectReassignmentEvents<TemplateMarker>(reassignment).ToList();

        var unclassified = events.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Value.Should().Be(60L, "the decode-failure offset comes from the created event, not the reassignment fallback");
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
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

    [Fact]
    public void ProjectReassignmentEvents_surfaces_typed_Unassigned_for_interface_marker()
    {
        var unassigned = new UnassignedEvent
        {
            ContractId = "00holding",
            TemplateId = new ProtoIdentifier { PackageId = "impl-pkg", ModuleName = "Token.Holding", EntityName = "Holding" },
            Source = "sync-src",
            Target = "sync-tgt",
            Offset = 70L,
        };
        var reassignment = new Reassignment { Offset = 70L };
        reassignment.Events.Add(new ReassignmentEvent { Unassigned = unassigned });

        var events = ContractStreamProjector.ProjectReassignmentEvents<InterfaceMarker>(reassignment).ToList();

        var typed = events.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<InterfaceMarker>.Unassigned>().Subject;
        typed.ContractId.Value.Should().Be("00holding");
        typed.Source.Id.Should().Be("sync-src");
        typed.Target.Id.Should().Be("sync-tgt");
    }

    [Fact]
    public void ProjectActiveContractEntry_IncompleteUnassigned_surfaces_Created_then_Unassigned_on_source_to_target()
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00holding",
            TemplateId = new ProtoIdentifier { PackageId = "tmpl-pkg", ModuleName = "Sample.Token", EntityName = "Holding" },
            CreateArguments = new ProtoRecord
            {
                Fields = { new RecordField { Label = "owner", Value = new ProtoValue { Party = "alice" } } },
            },
            Offset = 80L,
        };
        var response = new GetActiveContractsResponse
        {
            IncompleteUnassigned = new IncompleteUnassigned
            {
                CreatedEvent = created,
                UnassignedEvent = new UnassignedEvent
                {
                    ContractId = "00holding",
                    Source = "sync-src",
                    Target = "sync-tgt",
                    Offset = 81L,
                },
            },
        };

        var events = ContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(response).ToList();

        events.Should().HaveCount(2);
        var createdEvent = events[0].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>().Subject;
        createdEvent.ContractId.Value.Should().Be("00holding");
        createdEvent.SynchronizerId.Id.Should().Be("sync-src");
        var unassignedEvent = events[1].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unassigned>().Subject;
        unassignedEvent.ContractId.Value.Should().Be("00holding");
        unassignedEvent.Offset.Value.Should().Be(81L);
        unassignedEvent.Source.Id.Should().Be("sync-src");
        unassignedEvent.Target.Id.Should().Be("sync-tgt");
    }

    [Fact]
    public void ProjectActiveContractEntry_IncompleteUnassigned_surfaces_Unclassified_instead_of_the_Unassigned_when_Target_is_missing()
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00holding",
            TemplateId = new ProtoIdentifier { PackageId = "tmpl-pkg", ModuleName = "Sample.Token", EntityName = "Holding" },
            CreateArguments = new ProtoRecord(),
            Offset = 85L,
        };
        var response = new GetActiveContractsResponse
        {
            IncompleteUnassigned = new IncompleteUnassigned
            {
                CreatedEvent = created,
                UnassignedEvent = new UnassignedEvent
                {
                    ContractId = "00holding",
                    Source = "sync-src",
                    Target = string.Empty,
                    Offset = 86L,
                },
            },
        };

        var events = ContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(response).ToList();

        events.Should().HaveCount(2);
        events[0].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>();
        var unclassified = events[1].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Value.Should().Be(86L);
        unclassified.Kind.Should().Be(UnclassifiedKind.MissingSynchronizerId);
    }

    [Fact]
    public void ProjectActiveContractEntry_IncompleteUnassigned_omits_the_Unassigned_when_the_created_does_not_match_the_marker()
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00other",
            TemplateId = new ProtoIdentifier { PackageId = "other-pkg", ModuleName = "Other.Module", EntityName = "Other" },
            CreateArguments = new ProtoRecord(),
            Offset = 90L,
        };
        var response = new GetActiveContractsResponse
        {
            IncompleteUnassigned = new IncompleteUnassigned
            {
                CreatedEvent = created,
                UnassignedEvent = new UnassignedEvent
                {
                    ContractId = "00other",
                    Source = "sync-src",
                    Target = "sync-tgt",
                    Offset = 91L,
                },
            },
        };

        var events = ContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(response).ToList();

        var unclassified = events.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Value.Should().Be(90L);
        unclassified.Kind.Should().Be(UnclassifiedKind.CreatedEvent);
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
