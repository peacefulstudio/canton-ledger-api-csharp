// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Com.Daml.Ledger.Api.V2;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using AwesomeAssertions;
using Canton.Ledger.Testing.Helpers;
using Google.Rpc;
using Microsoft.Extensions.Logging;
using Xunit;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoArchivedEvent = Com.Daml.Ledger.Api.V2.ArchivedEvent;
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

        var events = InterfaceStreamProjector.ProjectTransactionEvents<InterfaceMarker, InterfaceMarkerView>(transaction).ToList();

        var typed = events.Should().ContainSingle().Subject
            .Should().BeOfType<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Created>().Subject;
        typed.ContractId.Value.Should().Be("00holding");
        typed.Payload.Amount.Should().Be("view-value");
    }

    [Theory]
    [MemberData(nameof(UndecodableInterfaceViews))]
    public void ProjectTransactionEvents_yields_Unclassified_when_interface_view_is_undecodable(InterfaceView undecodableView)
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00holding",
            TemplateId = new ProtoIdentifier { PackageId = "impl-pkg", ModuleName = "Token.Holding", EntityName = "Holding" },
            CreateArguments = LedgerClientTestFixtures.OwnerArguments(),
            Offset = 11L,
        };
        created.InterfaceViews.Add(undecodableView);
        var transaction = new Transaction { SynchronizerId = "sync-1" };
        transaction.Events.Add(new Event { Created = created });

        var events = InterfaceStreamProjector.ProjectTransactionEvents<InterfaceMarker, InterfaceMarkerView>(transaction).ToList();

        var unclassified = events.Should().ContainSingle().Subject
            .Should().BeOfType<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(11L));
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
            CreateArguments = LedgerClientTestFixtures.OwnerArguments(),
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

        var projected = InterfaceStreamProjector.ProjectActiveContractEntry<InterfaceMarker, InterfaceMarkerView>(response).ToList();

        var typed = projected.Should().ContainSingle().Subject
            .Should().BeOfType<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Created>().Subject;
        typed.Payload.Amount.Should().Be("acs-view-value");
    }

    [Theory]
    [MemberData(nameof(UndecodableInterfaceViews))]
    public void ProjectActiveContractEntry_yields_Unclassified_when_interface_view_is_undecodable(InterfaceView undecodableView)
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00holding",
            TemplateId = new ProtoIdentifier { PackageId = "impl-pkg", ModuleName = "Token.Holding", EntityName = "Holding" },
            CreateArguments = LedgerClientTestFixtures.OwnerArguments(),
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

        var projected = InterfaceStreamProjector.ProjectActiveContractEntry<InterfaceMarker, InterfaceMarkerView>(response).ToList();

        var unclassified = projected.Should().ContainSingle().Subject
            .Should().BeOfType<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(21L));
        unclassified.Kind.Should().Be(UnclassifiedKind.InterfaceViewUnavailable);
    }

    [Fact]
    public void ProjectReassignmentEvents_Assigned_decodes_Payload_from_matching_interface_view()
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00holding",
            TemplateId = new ProtoIdentifier { PackageId = "impl-pkg", ModuleName = "Token.Holding", EntityName = "Holding" },
            CreateArguments = LedgerClientTestFixtures.OwnerArguments(),
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

        var events = InterfaceStreamProjector.ProjectReassignmentEvents<InterfaceMarker, InterfaceMarkerView>(reassignment).ToList();

        var typed = events.Should().ContainSingle().Subject
            .Should().BeOfType<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Assigned>().Subject;
        typed.Payload.Amount.Should().Be("assigned-view-value");
    }

    private static Reassignment AssignedReassignmentWithUndecodableInterfaceView(
        InterfaceView undecodableView,
        long createdOffset,
        long reassignmentOffset)
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00holding",
            TemplateId = new ProtoIdentifier { PackageId = "impl-pkg", ModuleName = "Token.Holding", EntityName = "Holding" },
            CreateArguments = LedgerClientTestFixtures.OwnerArguments(),
            Offset = createdOffset,
        };
        created.InterfaceViews.Add(undecodableView);
        var reassignment = new Reassignment { Offset = reassignmentOffset };
        reassignment.Events.Add(new ReassignmentEvent
        {
            Assigned = new AssignedEvent
            {
                Source = "sync-src",
                Target = "sync-tgt",
                CreatedEvent = created,
            },
        });
        return reassignment;
    }

    [Theory]
    [MemberData(nameof(UndecodableInterfaceViews))]
    public void ProjectReassignmentEvents_Assigned_yields_Unclassified_when_interface_view_is_undecodable(InterfaceView undecodableView)
    {
        var reassignment = AssignedReassignmentWithUndecodableInterfaceView(
            undecodableView, createdOffset: 31L, reassignmentOffset: 31L);

        var events = InterfaceStreamProjector.ProjectReassignmentEvents<InterfaceMarker, InterfaceMarkerView>(reassignment).ToList();

        var unclassified = events.Should().ContainSingle().Subject
            .Should().BeOfType<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(31L));
        unclassified.Kind.Should().Be(UnclassifiedKind.InterfaceViewUnavailable);
    }

    [Theory]
    [MemberData(nameof(UndecodableInterfaceViews))]
    public void ProjectReassignmentEvents_Assigned_reports_an_unavailable_interface_view_carrying_no_offset_at_the_reassignment_offset(InterfaceView undecodableView)
    {
        var reassignment = AssignedReassignmentWithUndecodableInterfaceView(
            undecodableView, createdOffset: 0L, reassignmentOffset: 77L);

        var events = InterfaceStreamProjector.ProjectReassignmentEvents<InterfaceMarker, InterfaceMarkerView>(reassignment).ToList();

        var unclassified = events.Should().ContainSingle().Subject
            .Should().BeOfType<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(77L));
        unclassified.Kind.Should().Be(UnclassifiedKind.InterfaceViewUnavailable);
    }

    [Theory]
    [MemberData(nameof(UndecodableInterfaceViews))]
    public void ProjectReassignmentEvents_Assigned_prefers_the_created_offset_over_the_reassignment_offset_for_an_unavailable_interface_view(InterfaceView undecodableView)
    {
        var reassignment = AssignedReassignmentWithUndecodableInterfaceView(
            undecodableView, createdOffset: 21L, reassignmentOffset: 77L);

        var events = InterfaceStreamProjector.ProjectReassignmentEvents<InterfaceMarker, InterfaceMarkerView>(reassignment).ToList();

        var unclassified = events.Should().ContainSingle().Subject
            .Should().BeOfType<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(21L));
        unclassified.Kind.Should().Be(UnclassifiedKind.InterfaceViewUnavailable);
    }

    private static ProtoValue UnsetSumValue() => new();

    public static TheoryData<ProtoValue> PoisonValues =>
        new(LedgerClientTestFixtures.OutOfDecimalRangeNumeric(), UnsetSumValue());

    [Theory]
    [MemberData(nameof(PoisonValues))]
    public void ProjectTransactionEvents_surfaces_Created_with_undecodable_create_arguments_as_decode_failure_at_the_transactions_offset(ProtoValue poisonValue)
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00poison",
            TemplateId = new ProtoIdentifier { PackageId = "tmpl-pkg", ModuleName = "Sample.Token", EntityName = "Holding" },
            CreateArguments = LedgerClientTestFixtures.OwnerArgumentsWith("amount", poisonValue),
            Offset = 50L,
        };
        var transaction = new Transaction { Offset = 49L, SynchronizerId = "sync-1" };
        transaction.Events.Add(new Event { Created = created });

        var events = ContractStreamProjector.ProjectTransactionEvents<TemplateMarker>(transaction).ToList();

        var unclassified = events.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(49L));
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
    }

    [Theory]
    [MemberData(nameof(PoisonValues))]
    public void ProjectTransactionEvents_surfaces_Exercised_with_undecodable_choice_argument_as_decode_failure_at_the_transactions_offset(ProtoValue poisonValue)
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
        var transaction = new Transaction { Offset = 49L, SynchronizerId = "sync-1" };
        transaction.Events.Add(new Event { Exercised = exercised });

        var events = ContractStreamProjector.ProjectTransactionEvents<TemplateMarker>(transaction).ToList();

        var unclassified = events.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(49L));
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
    }

    [Theory]
    [MemberData(nameof(PoisonValues))]
    public void ProjectReassignmentEvents_surfaces_Assigned_with_undecodable_payload_as_decode_failure_at_the_reassignments_offset(ProtoValue poisonValue)
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00poison",
            TemplateId = new ProtoIdentifier { PackageId = "tmpl-pkg", ModuleName = "Sample.Token", EntityName = "Holding" },
            CreateArguments = LedgerClientTestFixtures.OwnerArgumentsWith("amount", poisonValue),
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
        unclassified.Offset.Should().Be(LedgerOffset.At(99L), "a decode failure resumes at the containing reassignment, not at the event that failed");
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
    }

    [Fact]
    public void ProjectTransactionEvents_surfaces_interface_event_carrying_no_template_id_as_DecodeFailure_at_the_transactions_offset()
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00no-template",
            Offset = 50L,
        };
        var transaction = new Transaction { Offset = 49L, SynchronizerId = "sync-1" };
        transaction.Events.Add(new Event { Created = created });

        var events = InterfaceStreamProjector.ProjectTransactionEvents<InterfaceMarker, InterfaceMarkerView>(transaction).ToList();

        var unclassified = events.Should().ContainSingle().Subject
            .Should().BeOfType<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(49L));
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
    }

    [Fact]
    public void ProjectReassignmentEvents_surfaces_interface_event_carrying_no_template_id_as_DecodeFailure_at_the_reassignments_offset()
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00no-template",
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

        var events = InterfaceStreamProjector.ProjectReassignmentEvents<InterfaceMarker, InterfaceMarkerView>(reassignment).ToList();

        var unclassified = events.Should().ContainSingle().Subject
            .Should().BeOfType<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(99L), "a decode failure resumes at the containing reassignment, not at the event that failed");
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
        typed.Payload.Owner.Should().Be("alice");
    }

    [Fact]
    public void ProjectTransactionEvents_populates_Created_Key_from_the_wire_contract_key_and_hash()
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00keyed",
            TemplateId = new ProtoIdentifier { PackageId = "tmpl-pkg", ModuleName = "Sample.Token", EntityName = "Holding" },
            CreateArguments = new ProtoRecord
            {
                Fields = { new RecordField { Label = "owner", Value = new ProtoValue { Party = "alice" } } },
            },
            ContractKey = new ProtoValue { Party = "alice" },
            ContractKeyHash = Google.Protobuf.ByteString.CopyFrom([0x01, 0x02, 0x03]),
            Offset = 41L,
        };
        var transaction = new Transaction { SynchronizerId = "sync-1" };
        transaction.Events.Add(new Event { Created = created });

        var typed = ContractStreamProjector.ProjectTransactionEvents<TemplateMarker>(transaction)
            .Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>().Subject;

        typed.Key.Should().NotBeNull(
            "a keyed template's materialization throws when the created event carries no key");
        typed.Key!.Value.Should().Be(new DamlParty("alice"));
        typed.Key.KeyHash.Should().Be(Convert.ToBase64String([0x01, 0x02, 0x03]));
    }

    [Fact]
    public void ProjectTransactionEvents_leaves_Created_Key_null_when_the_wire_carries_no_key()
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00unkeyed",
            TemplateId = new ProtoIdentifier { PackageId = "tmpl-pkg", ModuleName = "Sample.Token", EntityName = "Holding" },
            CreateArguments = new ProtoRecord
            {
                Fields = { new RecordField { Label = "owner", Value = new ProtoValue { Party = "alice" } } },
            },
            Offset = 42L,
        };
        var transaction = new Transaction { SynchronizerId = "sync-1" };
        transaction.Events.Add(new Event { Created = created });

        ContractStreamProjector.ProjectTransactionEvents<TemplateMarker>(transaction)
            .Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>().Subject
            .Key.Should().BeNull();
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

        var events = InterfaceStreamProjector.ProjectReassignmentEvents<InterfaceMarker, InterfaceMarkerView>(reassignment).ToList();

        var typed = events.Should().ContainSingle().Subject
            .Should().BeOfType<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Unassigned>().Subject;
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
            CreateArguments = LedgerClientTestFixtures.OwnerArguments(),
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
        unclassified.Offset.Should().Be(LedgerOffset.At(86L));
        unclassified.Kind.Should().Be(UnclassifiedKind.MissingSynchronizerId);
    }

    [Fact]
    public void ProjectActiveContractEntry_IncompleteUnassigned_omits_the_Unassigned_when_the_created_does_not_match_the_marker()
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00other",
            TemplateId = new ProtoIdentifier { PackageId = "other-pkg", ModuleName = "Other.Module", EntityName = "Other" },
            CreateArguments = LedgerClientTestFixtures.OwnerArguments(),
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
        unclassified.Offset.Should().Be(LedgerOffset.At(90L));
        unclassified.Kind.Should().Be(UnclassifiedKind.CreatedEvent);
    }

    private static GetActiveContractsResponse IncompleteUnassignedWithoutUnassignmentOffset(string target) =>
        new()
        {
            IncompleteUnassigned = new IncompleteUnassigned
            {
                CreatedEvent = new ProtoCreatedEvent
                {
                    ContractId = "00holding",
                    TemplateId = new ProtoIdentifier { PackageId = "tmpl-pkg", ModuleName = "Sample.Token", EntityName = "Holding" },
                    CreateArguments = new ProtoRecord
                    {
                        Fields = { new RecordField { Label = "owner", Value = new ProtoValue { Party = "alice" } } },
                    },
                    Offset = 80L,
                },
                UnassignedEvent = new UnassignedEvent
                {
                    ContractId = "00holding",
                    Source = "sync-src",
                    Target = target,
                },
            },
        };

    [Fact]
    public void ProjectActiveContractEntry_reports_an_unassignment_carrying_no_offset_at_the_snapshot_offset()
    {
        var response = IncompleteUnassignedWithoutUnassignmentOffset("sync-tgt");

        var events = ContractStreamProjector
            .ProjectActiveContractEntry<TemplateMarker>(response, logger: null, LedgerOffset.At(77L))
            .ToList();

        events.Should().HaveCount(2);
        events[0].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>();
        events[1].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unassigned>().Subject
            .Offset.Value.Should().Be(77L);
    }

    [Fact]
    public void ProjectActiveContractEntry_reports_an_unclassifiable_unassignment_carrying_no_offset_at_the_snapshot_offset()
    {
        var response = IncompleteUnassignedWithoutUnassignmentOffset(string.Empty);

        var events = ContractStreamProjector
            .ProjectActiveContractEntry<TemplateMarker>(response, logger: null, LedgerOffset.At(77L))
            .ToList();

        events.Should().HaveCount(2);
        events[0].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>();
        var unclassified = events[1].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(77L));
        unclassified.Kind.Should().Be(UnclassifiedKind.MissingSynchronizerId);
    }

    private static GetActiveContractsResponse ActiveContractEntryWithoutCreatedEvent(
        GetActiveContractsResponse.ContractEntryOneofCase entryCase) => entryCase switch
    {
        GetActiveContractsResponse.ContractEntryOneofCase.ActiveContract => new GetActiveContractsResponse
        {
            ActiveContract = new ActiveContract { SynchronizerId = "sync-1" },
        },
        GetActiveContractsResponse.ContractEntryOneofCase.IncompleteUnassigned => new GetActiveContractsResponse
        {
            IncompleteUnassigned = new IncompleteUnassigned
            {
                UnassignedEvent = new UnassignedEvent
                {
                    ContractId = "00holding",
                    Source = "sync-src",
                    Target = "sync-tgt",
                },
            },
        },
        GetActiveContractsResponse.ContractEntryOneofCase.IncompleteAssigned => new GetActiveContractsResponse
        {
            IncompleteAssigned = new IncompleteAssigned
            {
                AssignedEvent = new AssignedEvent { Source = "sync-src", Target = "sync-tgt" },
            },
        },
        _ => new GetActiveContractsResponse(),
    };

    [Theory]
    [InlineData(GetActiveContractsResponse.ContractEntryOneofCase.ActiveContract)]
    [InlineData(GetActiveContractsResponse.ContractEntryOneofCase.IncompleteUnassigned)]
    [InlineData(GetActiveContractsResponse.ContractEntryOneofCase.IncompleteAssigned)]
    [InlineData(GetActiveContractsResponse.ContractEntryOneofCase.None)]
    public void ProjectActiveContractEntry_reports_an_entry_without_a_created_event_at_the_snapshot_offset(
        GetActiveContractsResponse.ContractEntryOneofCase entryCase)
    {
        var response = ActiveContractEntryWithoutCreatedEvent(entryCase);

        var events = ContractStreamProjector
            .ProjectActiveContractEntry<TemplateMarker>(response, logger: null, LedgerOffset.At(77L))
            .ToList();

        var unclassified = events.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(77L));
        unclassified.Kind.Should().Be(UnclassifiedKind.Unknown);
        unclassified.RawKind.Should().Be(entryCase.ToString());
    }

    [Theory]
    [InlineData(GetActiveContractsResponse.ContractEntryOneofCase.ActiveContract)]
    [InlineData(GetActiveContractsResponse.ContractEntryOneofCase.IncompleteUnassigned)]
    [InlineData(GetActiveContractsResponse.ContractEntryOneofCase.IncompleteAssigned)]
    [InlineData(GetActiveContractsResponse.ContractEntryOneofCase.None)]
    public void ProjectActiveContractEntry_falls_back_to_the_begin_of_the_ledger_without_a_snapshot_offset(
        GetActiveContractsResponse.ContractEntryOneofCase entryCase)
    {
        var response = ActiveContractEntryWithoutCreatedEvent(entryCase);

        var events = ContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(response).ToList();

        events.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject
            .Offset.Should().Be(LedgerOffset.Begin);
    }

    [Fact]
    public void ProjectActiveContractEntry_prefers_the_unassignment_offset_over_the_snapshot_offset()
    {
        var response = new GetActiveContractsResponse
        {
            IncompleteUnassigned = new IncompleteUnassigned
            {
                UnassignedEvent = new UnassignedEvent
                {
                    ContractId = "00holding",
                    Source = "sync-src",
                    Target = "sync-tgt",
                    Offset = 50L,
                },
            },
        };

        var events = ContractStreamProjector
            .ProjectActiveContractEntry<TemplateMarker>(response, logger: null, LedgerOffset.At(77L))
            .ToList();

        var unclassified = events.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(50L));
        unclassified.Kind.Should().Be(UnclassifiedKind.Unknown);
    }

    [Fact]
    public void ProjectActiveContractEntry_reports_a_created_event_carrying_no_offset_at_the_snapshot_offset()
    {
        var response = new GetActiveContractsResponse
        {
            ActiveContract = new ActiveContract
            {
                CreatedEvent = new ProtoCreatedEvent
                {
                    ContractId = "00other",
                    TemplateId = new ProtoIdentifier { PackageId = "other-pkg", ModuleName = "Other.Module", EntityName = "Other" },
                    CreateArguments = LedgerClientTestFixtures.OwnerArguments(),
                },
                SynchronizerId = "sync-1",
            },
        };

        var events = ContractStreamProjector
            .ProjectActiveContractEntry<TemplateMarker>(response, logger: null, LedgerOffset.At(77L))
            .ToList();

        var unclassified = events.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(77L));
        unclassified.Kind.Should().Be(UnclassifiedKind.CreatedEvent);
    }

    [Fact]
    public void ProjectActiveContractEntry_reports_an_undecodable_entry_carrying_no_offset_at_the_snapshot_offset()
    {
        var response = new GetActiveContractsResponse
        {
            ActiveContract = new ActiveContract
            {
                CreatedEvent = new ProtoCreatedEvent
                {
                    ContractId = "00holding",
                    TemplateId = new ProtoIdentifier { PackageId = "tmpl-pkg", ModuleName = "Sample.Token", EntityName = "Holding" },
                    CreateArguments = new ProtoRecord
                    {
                        Fields =
                        {
                            new RecordField
                            {
                                Label = "amount",
                                Value = LedgerClientTestFixtures.OutOfDecimalRangeNumeric(),
                            },
                        },
                    },
                },
                SynchronizerId = "sync-1",
            },
        };

        var events = ContractStreamProjector
            .ProjectActiveContractEntry<TemplateMarker>(response, logger: null, LedgerOffset.At(77L))
            .ToList();

        var unclassified = events.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(77L));
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
    }

    private static GetActiveContractsResponse ActiveContractEntryWithUndecodableInterfaceView(
        InterfaceView undecodableView,
        long createdOffset)
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00holding",
            TemplateId = new ProtoIdentifier { PackageId = "impl-pkg", ModuleName = "Token.Holding", EntityName = "Holding" },
            CreateArguments = LedgerClientTestFixtures.OwnerArguments(),
            Offset = createdOffset,
        };
        created.InterfaceViews.Add(undecodableView);
        return new GetActiveContractsResponse
        {
            ActiveContract = new ActiveContract
            {
                CreatedEvent = created,
                SynchronizerId = "sync-1",
            },
        };
    }

    [Theory]
    [MemberData(nameof(UndecodableInterfaceViews))]
    public void ProjectActiveContractEntry_reports_an_unavailable_interface_view_carrying_no_offset_at_the_snapshot_offset(InterfaceView undecodableView)
    {
        var response = ActiveContractEntryWithUndecodableInterfaceView(undecodableView, createdOffset: 0L);

        var projected = InterfaceStreamProjector
            .ProjectActiveContractEntry<InterfaceMarker, InterfaceMarkerView>(response, logger: null, LedgerOffset.At(77L))
            .ToList();

        var unclassified = projected.Should().ContainSingle().Subject
            .Should().BeOfType<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(77L));
        unclassified.Kind.Should().Be(UnclassifiedKind.InterfaceViewUnavailable);
    }

    [Theory]
    [MemberData(nameof(UndecodableInterfaceViews))]
    public void ProjectActiveContractEntry_prefers_the_created_offset_over_the_snapshot_offset_for_an_unavailable_interface_view(InterfaceView undecodableView)
    {
        var response = ActiveContractEntryWithUndecodableInterfaceView(undecodableView, createdOffset: 21L);

        var projected = InterfaceStreamProjector
            .ProjectActiveContractEntry<InterfaceMarker, InterfaceMarkerView>(response, logger: null, LedgerOffset.At(77L))
            .ToList();

        var unclassified = projected.Should().ContainSingle().Subject
            .Should().BeOfType<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(21L));
        unclassified.Kind.Should().Be(UnclassifiedKind.InterfaceViewUnavailable);
    }

    private static GetActiveContractsResponse IncompleteAssignedEntryWithUndecodableInterfaceView(
        InterfaceView undecodableView,
        long createdOffset)
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00holding",
            TemplateId = new ProtoIdentifier { PackageId = "impl-pkg", ModuleName = "Token.Holding", EntityName = "Holding" },
            CreateArguments = LedgerClientTestFixtures.OwnerArguments(),
            Offset = createdOffset,
        };
        created.InterfaceViews.Add(undecodableView);
        return new GetActiveContractsResponse
        {
            IncompleteAssigned = new IncompleteAssigned
            {
                AssignedEvent = new AssignedEvent
                {
                    Source = "sync-src",
                    Target = "sync-tgt",
                    CreatedEvent = created,
                },
            },
        };
    }

    [Theory]
    [MemberData(nameof(UndecodableInterfaceViews))]
    public void ProjectActiveContractEntry_IncompleteAssigned_reports_an_unavailable_interface_view_carrying_no_offset_at_the_snapshot_offset(InterfaceView undecodableView)
    {
        var response = IncompleteAssignedEntryWithUndecodableInterfaceView(undecodableView, createdOffset: 0L);

        var projected = InterfaceStreamProjector
            .ProjectActiveContractEntry<InterfaceMarker, InterfaceMarkerView>(response, logger: null, LedgerOffset.At(77L))
            .ToList();

        var unclassified = projected.Should().ContainSingle().Subject
            .Should().BeOfType<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(77L));
        unclassified.Kind.Should().Be(UnclassifiedKind.InterfaceViewUnavailable);
    }

    [Theory]
    [MemberData(nameof(UndecodableInterfaceViews))]
    public void ProjectActiveContractEntry_IncompleteAssigned_prefers_the_created_offset_over_the_snapshot_offset_for_an_unavailable_interface_view(InterfaceView undecodableView)
    {
        var response = IncompleteAssignedEntryWithUndecodableInterfaceView(undecodableView, createdOffset: 21L);

        var projected = InterfaceStreamProjector
            .ProjectActiveContractEntry<InterfaceMarker, InterfaceMarkerView>(response, logger: null, LedgerOffset.At(77L))
            .ToList();

        var unclassified = projected.Should().ContainSingle().Subject
            .Should().BeOfType<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(21L));
        unclassified.Kind.Should().Be(UnclassifiedKind.InterfaceViewUnavailable);
    }

    [Fact]
    public void ProjectTransactionEvents_separates_an_archived_event_without_a_TemplateId_from_a_different_template_archive()
    {
        var transaction = new Transaction { Offset = 100L, SynchronizerId = "sync-1" };
        transaction.Events.Add(new Event
        {
            Archived = new ProtoArchivedEvent
            {
                ContractId = "00other",
                TemplateId = new ProtoIdentifier { PackageId = "other-pkg", ModuleName = "Other.Module", EntityName = "Other" },
                Offset = 100L,
            },
        });
        transaction.Events.Add(new Event
        {
            Archived = new ProtoArchivedEvent { ContractId = "00holding", Offset = 100L },
        });
        var loggerFactory = new CapturingLoggerFactory();

        var events = ContractStreamProjector
            .ProjectTransactionEvents<TemplateMarker>(transaction, loggerFactory.CreateLogger("test"))
            .ToList();

        events.Should().HaveCount(2);
        events[0].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>()
            .Which.Kind.Should().Be(UnclassifiedKind.ArchivedEvent);
        events[1].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>()
            .Which.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        loggerFactory.Records.Should().ContainSingle(record => record.Level == LogLevel.Warning);
    }

    [Fact]
    public void ProjectReassignmentEvents_separates_an_unassigned_event_without_a_TemplateId_from_a_different_template_unassign()
    {
        var reassignment = new Reassignment { Offset = 110L };
        reassignment.Events.Add(new ReassignmentEvent
        {
            Unassigned = new UnassignedEvent
            {
                ContractId = "00other",
                TemplateId = new ProtoIdentifier { PackageId = "other-pkg", ModuleName = "Other.Module", EntityName = "Other" },
                Source = "sync-src",
                Target = "sync-tgt",
                Offset = 110L,
            },
        });
        reassignment.Events.Add(new ReassignmentEvent
        {
            Unassigned = new UnassignedEvent
            {
                ContractId = "00holding",
                Source = "sync-src",
                Target = "sync-tgt",
                Offset = 110L,
            },
        });
        var loggerFactory = new CapturingLoggerFactory();

        var events = ContractStreamProjector
            .ProjectReassignmentEvents<TemplateMarker>(reassignment, loggerFactory.CreateLogger("test"))
            .ToList();

        events.Should().HaveCount(2);
        events[0].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>()
            .Which.Kind.Should().Be(UnclassifiedKind.UnassignedEvent);
        events[1].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>()
            .Which.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        loggerFactory.Records.Should().ContainSingle(record => record.Level == LogLevel.Warning);
    }

    [Fact]
    public void ProjectTransactionEvents_separates_a_created_event_without_a_TemplateId_from_a_different_template_create()
    {
        var transaction = new Transaction { Offset = 120L, SynchronizerId = "sync-1" };
        transaction.Events.Add(new Event
        {
            Created = new ProtoCreatedEvent
            {
                ContractId = "00other",
                TemplateId = new ProtoIdentifier { PackageId = "other-pkg", ModuleName = "Other.Module", EntityName = "Other" },
                CreateArguments = LedgerClientTestFixtures.OwnerArguments(),
                Offset = 120L,
            },
        });
        transaction.Events.Add(new Event
        {
            Created = new ProtoCreatedEvent { ContractId = "00holding", CreateArguments = LedgerClientTestFixtures.OwnerArguments(), Offset = 120L },
        });
        var loggerFactory = new CapturingLoggerFactory();

        var events = ContractStreamProjector
            .ProjectTransactionEvents<TemplateMarker>(transaction, loggerFactory.CreateLogger("test"))
            .ToList();

        events.Should().HaveCount(2);
        events[0].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>()
            .Which.Kind.Should().Be(UnclassifiedKind.CreatedEvent);
        events[1].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>()
            .Which.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        loggerFactory.Records.Should().ContainSingle(record => record.Level == LogLevel.Warning);
    }

    [Fact]
    public void ProjectTransactionEvents_separates_an_exercised_event_without_a_TemplateId_from_a_different_template_exercise()
    {
        var transaction = new Transaction { Offset = 130L, SynchronizerId = "sync-1" };
        transaction.Events.Add(new Event
        {
            Exercised = new ProtoExercisedEvent
            {
                ContractId = "00other",
                TemplateId = new ProtoIdentifier { PackageId = "other-pkg", ModuleName = "Other.Module", EntityName = "Other" },
                Choice = "Accept",
                Consuming = true,
                Offset = 130L,
            },
        });
        transaction.Events.Add(new Event
        {
            Exercised = new ProtoExercisedEvent { ContractId = "00holding", Choice = "Accept", Consuming = true, Offset = 130L },
        });
        var loggerFactory = new CapturingLoggerFactory();

        var events = ContractStreamProjector
            .ProjectTransactionEvents<TemplateMarker>(transaction, loggerFactory.CreateLogger("test"))
            .ToList();

        events.Should().HaveCount(2);
        events[0].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>()
            .Which.Kind.Should().Be(UnclassifiedKind.ExercisedEvent);
        events[1].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>()
            .Which.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        loggerFactory.Records.Should().ContainSingle(record => record.Level == LogLevel.Warning);
    }

    [Fact]
    public void ProjectReassignmentEvents_separates_an_assigned_event_without_a_TemplateId_from_a_different_template_assign()
    {
        var reassignment = new Reassignment { Offset = 140L };
        reassignment.Events.Add(new ReassignmentEvent
        {
            Assigned = new AssignedEvent
            {
                Source = "sync-src",
                Target = "sync-tgt",
                CreatedEvent = new ProtoCreatedEvent
                {
                    ContractId = "00other",
                    TemplateId = new ProtoIdentifier { PackageId = "other-pkg", ModuleName = "Other.Module", EntityName = "Other" },
                    CreateArguments = LedgerClientTestFixtures.OwnerArguments(),
                    Offset = 140L,
                },
            },
        });
        reassignment.Events.Add(new ReassignmentEvent
        {
            Assigned = new AssignedEvent
            {
                Source = "sync-src",
                Target = "sync-tgt",
                CreatedEvent = new ProtoCreatedEvent { ContractId = "00holding", CreateArguments = LedgerClientTestFixtures.OwnerArguments(), Offset = 140L },
            },
        });
        var loggerFactory = new CapturingLoggerFactory();

        var events = ContractStreamProjector
            .ProjectReassignmentEvents<TemplateMarker>(reassignment, loggerFactory.CreateLogger("test"))
            .ToList();

        events.Should().HaveCount(2);
        events[0].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>()
            .Which.Kind.Should().Be(UnclassifiedKind.AssignedEvent);
        events[1].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>()
            .Which.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        loggerFactory.Records.Should().ContainSingle(record => record.Level == LogLevel.Warning);
    }

    [Fact]
    public void ProjectActiveContractEntry_keeps_the_snapshot_running_when_an_incomplete_unassigned_carries_no_contract_id()
    {
        var response = new GetActiveContractsResponse
        {
            IncompleteUnassigned = new IncompleteUnassigned
            {
                CreatedEvent = new ProtoCreatedEvent
                {
                    ContractId = "00holding",
                    TemplateId = new ProtoIdentifier { PackageId = "tmpl-pkg", ModuleName = "Sample.Token", EntityName = "Holding" },
                    CreateArguments = LedgerClientTestFixtures.OwnerArguments(),
                    Offset = 42L,
                },
                UnassignedEvent = new UnassignedEvent
                {
                    ContractId = string.Empty,
                    Source = "sync-1",
                    Target = "sync-2",
                    Offset = 43L,
                    ReassignmentId = "reassignment-1",
                    ReassignmentCounter = 7UL,
                },
            },
        };
        var loggerFactory = new CapturingLoggerFactory();

        var events = ContractStreamProjector
            .ProjectActiveContractEntry<TemplateMarker>(response, loggerFactory.CreateLogger("test"))
            .ToList();

        events.Should().HaveCount(2);
        events[0].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>();
        var unclassified = events[1].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        unclassified.Offset.Should().Be(LedgerOffset.At(43L));
        loggerFactory.Records.Should().ContainSingle(record => record.Level == LogLevel.Warning);
    }
}
