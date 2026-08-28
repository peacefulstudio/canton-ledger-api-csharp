// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Xunit;

namespace Canton.Ledger.Testing.Helpers;

/// <summary>
/// Behavioural parity suite over the active-contract classification policy, run against every
/// transport's <c>ContractStreamProjector</c> through one shared set of test bodies. It pins the
/// no-silent-drop decision tree that both transports delegate to a single
/// implementation: which shape becomes which <see cref="UnclassifiedKind"/>, the missing-
/// synchronizer rule, the order the two are reported in, how many events each entry shape
/// projects to, and the offset each projected event is reported at — including the fallback an
/// entry that carries no offset of its own lands on when no snapshot offset is supplied.
///
/// A second lane runs the same entry shapes subscribed as an <em>interface</em> marker, pinning
/// that both transports project the participant-computed interface view — never the implementing
/// template's create argument — onto an interface row, and agree on which view shapes leave the
/// row unclassified. Wire decoding stays per-transport and is covered by each transport's own
/// suite.
/// </summary>
public abstract class ContractStreamProjectorParityTests
{
    /// <summary>
    /// Renders <paramref name="scenario"/> into this transport's wire shape, runs it through that
    /// transport's active-contract projection, and returns the projected events in order.
    /// </summary>
    protected abstract Task<IReadOnlyList<ContractStreamEvent<TemplateMarker>>> ProjectActiveContractEntryAsync(
        ActiveContractScenario scenario);

    /// <summary>
    /// Renders <paramref name="scenario"/> into this transport's wire shape, runs it through that
    /// transport's active-contract projection subscribed as an interface marker, and returns the
    /// projected events in order.
    /// </summary>
    protected abstract Task<IReadOnlyList<ContractStreamEvent<InterfaceMarker>>> ProjectActiveContractEntryAsInterfaceAsync(
        ActiveContractScenario scenario);

    private async Task<ContractStreamEvent<TemplateMarker>> ProjectOnlyEventAsync(ActiveContractScenario scenario)
    {
        var projected = await ProjectActiveContractEntryAsync(scenario);
        return projected.Should().ContainSingle().Subject;
    }

    private static void ShouldBeTheCreatedScopedToTheEntrysSynchronizer(ContractStreamEvent<TemplateMarker> projected)
    {
        var created = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>().Subject;
        created.ContractId.Value.Should().Be(ActiveContractScenario.ContractId);
        created.Offset.Should().Be(LedgerOffset.At(ActiveContractScenario.CreatedOffset));
        created.SynchronizerId.Should().Be(new SynchronizerId(ActiveContractScenario.SynchronizerId));
    }

    [Theory]
    [InlineData(ActiveContractEntry.Active)]
    [InlineData(ActiveContractEntry.IncompleteAssigned)]
    public async Task ProjectActiveContractEntry_classifies_a_matching_entry_as_Created_scoped_to_the_entrys_synchronizer(
        ActiveContractEntry entry)
    {
        var projected = await ProjectOnlyEventAsync(new ActiveContractScenario { Entry = entry });

        ShouldBeTheCreatedScopedToTheEntrysSynchronizer(projected);
    }

    [Fact]
    public async Task ProjectActiveContractEntry_follows_a_matching_IncompleteUnassigneds_Created_with_its_Unassigned()
    {
        var projected = await ProjectActiveContractEntryAsync(
            new ActiveContractScenario { Entry = ActiveContractEntry.IncompleteUnassigned });

        projected.Should().HaveCount(2);
        ShouldBeTheCreatedScopedToTheEntrysSynchronizer(projected[0]);
        var unassigned = projected[1].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unassigned>().Subject;
        unassigned.ContractId.Value.Should().Be(ActiveContractScenario.ContractId);
        unassigned.Offset.Should().Be(LedgerOffset.At(ActiveContractScenario.UnassignedOffset));
        unassigned.Source.Should().Be(new SynchronizerId(ActiveContractScenario.SynchronizerId));
        unassigned.Target.Should().Be(new SynchronizerId(ActiveContractScenario.CounterpartSynchronizerId));
        unassigned.ReassignmentId.Should().Be(ActiveContractScenario.ReassignmentId);
        unassigned.ReassignmentCounter.Should().Be(ActiveContractScenario.ReassignmentCounter);
    }

    [Theory]
    [InlineData(ActiveContractEntry.Active)]
    [InlineData(ActiveContractEntry.IncompleteUnassigned)]
    [InlineData(ActiveContractEntry.IncompleteAssigned)]
    public async Task ProjectActiveContractEntry_surfaces_an_entry_that_does_not_match_the_marker_as_Unclassified_CreatedEvent(
        ActiveContractEntry entry)
    {
        var projected = await ProjectOnlyEventAsync(new ActiveContractScenario
        {
            Entry = entry,
            EntityName = ActiveContractScenario.OtherEntityName,
        });

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Kind.Should().Be(UnclassifiedKind.CreatedEvent);
        unclassified.Offset.Should().Be(LedgerOffset.At(ActiveContractScenario.CreatedOffset));
    }

    [Theory]
    [InlineData(ActiveContractEntry.Active)]
    [InlineData(ActiveContractEntry.IncompleteUnassigned)]
    [InlineData(ActiveContractEntry.IncompleteAssigned)]
    public async Task ProjectActiveContractEntry_surfaces_an_entry_carrying_no_synchronizer_as_Unclassified_MissingSynchronizerId(
        ActiveContractEntry entry)
    {
        var projected = await ProjectOnlyEventAsync(new ActiveContractScenario { Entry = entry, Synchronizer = null });

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Kind.Should().Be(UnclassifiedKind.MissingSynchronizerId);
        unclassified.Offset.Should().Be(LedgerOffset.At(ActiveContractScenario.CreatedOffset));
    }

    [Theory]
    [InlineData(ActiveContractEntry.Active)]
    [InlineData(ActiveContractEntry.IncompleteUnassigned)]
    [InlineData(ActiveContractEntry.IncompleteAssigned)]
    public async Task ProjectActiveContractEntry_reports_a_marker_mismatch_ahead_of_a_missing_synchronizer(
        ActiveContractEntry entry)
    {
        var projected = await ProjectOnlyEventAsync(new ActiveContractScenario
        {
            Entry = entry,
            EntityName = ActiveContractScenario.OtherEntityName,
            Synchronizer = null,
        });

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Kind.Should().Be(UnclassifiedKind.CreatedEvent);
    }

    [Theory]
    [InlineData(ActiveContractEntry.Active)]
    [InlineData(ActiveContractEntry.IncompleteUnassigned)]
    [InlineData(ActiveContractEntry.IncompleteAssigned)]
    public async Task ProjectActiveContractEntry_surfaces_an_entry_without_a_created_event_as_Unclassified_Unknown(
        ActiveContractEntry entry)
    {
        var projected = await ProjectOnlyEventAsync(new ActiveContractScenario { Entry = entry, OmitCreatedEvent = true });

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Kind.Should().Be(UnclassifiedKind.Unknown);
        unclassified.RawKind.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(ActiveContractEntry.Active)]
    [InlineData(ActiveContractEntry.IncompleteUnassigned)]
    [InlineData(ActiveContractEntry.IncompleteAssigned)]
    public async Task ProjectActiveContractEntry_reports_an_entry_without_a_created_event_at_the_offset_the_entry_carries(
        ActiveContractEntry entry)
    {
        var expectedOffset = entry == ActiveContractEntry.IncompleteUnassigned
            ? LedgerOffset.At(ActiveContractScenario.UnassignedOffset)
            : LedgerOffset.Begin;

        var projected = await ProjectOnlyEventAsync(new ActiveContractScenario { Entry = entry, OmitCreatedEvent = true });

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(expectedOffset);
    }

    private Task<IReadOnlyList<ContractStreamEvent<InterfaceMarker>>> ProjectImplementingEntryAsync(
        ActiveContractEntry entry, InterfaceViewRendering interfaceView) =>
        ProjectActiveContractEntryAsInterfaceAsync(new ActiveContractScenario
        {
            Entry = entry,
            EntityName = ActiveContractScenario.OtherEntityName,
            InterfaceView = interfaceView,
        });

    private static void ShouldCarryTheComputedInterfaceView(ContractStreamEvent<InterfaceMarker> projected)
    {
        var created = projected.Should().BeOfType<ContractStreamEvent<InterfaceMarker>.Created>().Subject;
        created.ContractId.Value.Should().Be(ActiveContractScenario.ContractId);
        created.Offset.Should().Be(LedgerOffset.At(ActiveContractScenario.CreatedOffset));
        created.SynchronizerId.Should().Be(new SynchronizerId(ActiveContractScenario.SynchronizerId));
        created.Payload.GetRequiredField(ActiveContractScenario.PayloadFieldName).As<DamlText>().Value
            .Should().Be(ActiveContractScenario.InterfaceViewValue);
    }

    [Theory]
    [InlineData(ActiveContractEntry.Active)]
    [InlineData(ActiveContractEntry.IncompleteAssigned)]
    public async Task ProjectActiveContractEntry_decodes_the_participant_computed_view_for_an_interface_marker(
        ActiveContractEntry entry)
    {
        var projected = await ProjectImplementingEntryAsync(entry, InterfaceViewRendering.Computed);

        ShouldCarryTheComputedInterfaceView(projected.Should().ContainSingle().Subject);
    }

    [Theory]
    [InlineData(ActiveContractEntry.Active)]
    [InlineData(ActiveContractEntry.IncompleteAssigned)]
    public async Task ProjectActiveContractEntry_never_projects_the_implementing_templates_create_argument_onto_an_interface_row(
        ActiveContractEntry entry)
    {
        var projected = await ProjectImplementingEntryAsync(entry, InterfaceViewRendering.Computed);

        var created = projected.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<InterfaceMarker>.Created>().Subject;
        created.Payload.GetRequiredField(ActiveContractScenario.PayloadFieldName).As<DamlText>().Value
            .Should().NotBe(ActiveContractScenario.CreateArgumentValue);
    }

    [Fact]
    public async Task ProjectActiveContractEntry_follows_an_interface_matching_IncompleteUnassigneds_Created_with_its_Unassigned()
    {
        var projected = await ProjectImplementingEntryAsync(
            ActiveContractEntry.IncompleteUnassigned, InterfaceViewRendering.Computed);

        projected.Should().HaveCount(2);
        ShouldCarryTheComputedInterfaceView(projected[0]);
        var unassigned = projected[1].Should().BeOfType<ContractStreamEvent<InterfaceMarker>.Unassigned>().Subject;
        unassigned.ContractId.Value.Should().Be(ActiveContractScenario.ContractId);
        unassigned.Offset.Should().Be(LedgerOffset.At(ActiveContractScenario.UnassignedOffset));
        unassigned.Source.Should().Be(new SynchronizerId(ActiveContractScenario.SynchronizerId));
        unassigned.Target.Should().Be(new SynchronizerId(ActiveContractScenario.CounterpartSynchronizerId));
        unassigned.ReassignmentId.Should().Be(ActiveContractScenario.ReassignmentId);
        unassigned.ReassignmentCounter.Should().Be(ActiveContractScenario.ReassignmentCounter);
    }

    [Theory]
    [InlineData(ActiveContractEntry.Active, InterfaceViewRendering.ComputationFailed)]
    [InlineData(ActiveContractEntry.Active, InterfaceViewRendering.ValueOmitted)]
    [InlineData(ActiveContractEntry.IncompleteUnassigned, InterfaceViewRendering.ComputationFailed)]
    [InlineData(ActiveContractEntry.IncompleteUnassigned, InterfaceViewRendering.ValueOmitted)]
    [InlineData(ActiveContractEntry.IncompleteAssigned, InterfaceViewRendering.ComputationFailed)]
    [InlineData(ActiveContractEntry.IncompleteAssigned, InterfaceViewRendering.ValueOmitted)]
    public async Task ProjectActiveContractEntry_surfaces_an_undecodable_interface_view_as_Unclassified_InterfaceViewUnavailable(
        ActiveContractEntry entry, InterfaceViewRendering interfaceView)
    {
        var projected = await ProjectImplementingEntryAsync(entry, interfaceView);

        var unclassified = projected.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<InterfaceMarker>.Unclassified>().Subject;
        unclassified.Kind.Should().Be(UnclassifiedKind.InterfaceViewUnavailable);
    }

    [Theory]
    [InlineData(ActiveContractEntry.Active)]
    [InlineData(ActiveContractEntry.IncompleteUnassigned)]
    [InlineData(ActiveContractEntry.IncompleteAssigned)]
    public async Task ProjectActiveContractEntry_surfaces_an_entry_carrying_no_interface_view_as_Unclassified_CreatedEvent(
        ActiveContractEntry entry)
    {
        var projected = await ProjectImplementingEntryAsync(entry, InterfaceViewRendering.None);

        var unclassified = projected.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<InterfaceMarker>.Unclassified>().Subject;
        unclassified.Kind.Should().Be(UnclassifiedKind.CreatedEvent);
        unclassified.Offset.Should().Be(LedgerOffset.At(ActiveContractScenario.CreatedOffset));
    }
}
