// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Contracts;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Testing.Helpers;

/// <summary>
/// Behavioural parity suite over marker matching, run against every transport's
/// <c>MarkerMatcher</c> through one shared set of test bodies. It pins the decisions both
/// transports must agree on: that a marker is matched by module and entity name regardless of
/// package, that an interface marker is matched only through an identity the event implements,
/// that an unassigned event needs no client-side interface check because the participant's
/// reassignment filter already scoped it, and the package-name reference format a stream filter
/// names the marker by. The wire encodings themselves stay per-transport.
/// </summary>
public abstract class MarkerMatchingParityTests
{
    /// <summary>Whether this transport's matcher treats <paramref name="marker"/> as an interface.</summary>
    protected abstract bool IsInterfaceMarker(DamlTypeKind marker);

    /// <summary>The identifier this transport puts on a stream filter scoped to <paramref name="marker"/>.</summary>
    protected abstract RuntimeIdentifier StreamFilterIdentifier(DamlTypeKind marker);

    /// <summary>
    /// Renders <paramref name="scenario"/> into this transport's wire shape and reports whether
    /// this transport's matcher classifies it as <paramref name="marker"/>.
    /// </summary>
    protected abstract bool MatchesWireEvent(DamlTypeKind marker, MarkerMatchScenario scenario);

    /// <summary>
    /// Reports whether this transport's matcher classifies an already-neutral
    /// <paramref name="created"/> contract as <paramref name="marker"/>.
    /// </summary>
    protected abstract bool MatchesCreatedContract(DamlTypeKind marker, CreatedContract created);

    [Fact]
    public void IsInterface_distinguishes_an_interface_marker_from_a_template_marker()
    {
        IsInterfaceMarker(DamlTypeKind.Template).Should().BeFalse();
        IsInterfaceMarker(DamlTypeKind.Interface).Should().BeTrue();
    }

    [Fact]
    public void StreamFilterIdentifier_names_a_template_marker_by_package_name_reference()
    {
        var identifier = StreamFilterIdentifier(DamlTypeKind.Template);

        identifier.PackageId.Should().Be($"#{TemplateMarker.PackageName}");
        identifier.ModuleName.Should().Be(TemplateMarker.TemplateId.ModuleName);
        identifier.EntityName.Should().Be(TemplateMarker.TemplateId.EntityName);
    }

    [Fact]
    public void StreamFilterIdentifier_names_an_interface_marker_by_package_name_reference()
    {
        var identifier = StreamFilterIdentifier(DamlTypeKind.Interface);

        identifier.PackageId.Should().Be($"#{InterfaceMarker.PackageName}");
        identifier.ModuleName.Should().Be(InterfaceMarker.InterfaceId.ModuleName);
        identifier.EntityName.Should().Be(InterfaceMarker.InterfaceId.EntityName);
    }

    [Theory]
    [InlineData(MarkerWireEvent.Created)]
    [InlineData(MarkerWireEvent.Archived)]
    [InlineData(MarkerWireEvent.Exercised)]
    [InlineData(MarkerWireEvent.Unassigned)]
    public void MatchesWireEvent_matches_a_template_marker_by_module_and_entity_name_across_packages(
        MarkerWireEvent wireEvent)
    {
        MatchesWireEvent(DamlTypeKind.Template, new MarkerMatchScenario { Event = wireEvent })
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(MarkerWireEvent.Created)]
    [InlineData(MarkerWireEvent.Archived)]
    [InlineData(MarkerWireEvent.Exercised)]
    [InlineData(MarkerWireEvent.Unassigned)]
    public void MatchesWireEvent_does_not_match_a_template_marker_on_a_different_entity_name(
        MarkerWireEvent wireEvent)
    {
        MatchesWireEvent(
            DamlTypeKind.Template,
            new MarkerMatchScenario { Event = wireEvent, TemplateId = MarkerMatchScenario.OtherTemplateId })
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(MarkerWireEvent.Created)]
    [InlineData(MarkerWireEvent.Archived)]
    [InlineData(MarkerWireEvent.Exercised)]
    public void MatchesWireEvent_matches_an_interface_marker_through_an_implemented_interface(
        MarkerWireEvent wireEvent)
    {
        MatchesWireEvent(
            DamlTypeKind.Interface,
            new MarkerMatchScenario
            {
                Event = wireEvent,
                ImplementedInterface = MarkerMatchScenario.MatchingInterfaceId,
            })
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(MarkerWireEvent.Created)]
    [InlineData(MarkerWireEvent.Archived)]
    [InlineData(MarkerWireEvent.Exercised)]
    public void MatchesWireEvent_does_not_match_an_interface_marker_through_a_different_implemented_interface(
        MarkerWireEvent wireEvent)
    {
        MatchesWireEvent(
            DamlTypeKind.Interface,
            new MarkerMatchScenario
            {
                Event = wireEvent,
                ImplementedInterface = MarkerMatchScenario.OtherInterfaceId,
            })
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(MarkerWireEvent.Created)]
    [InlineData(MarkerWireEvent.Archived)]
    [InlineData(MarkerWireEvent.Exercised)]
    public void MatchesWireEvent_does_not_match_an_interface_marker_on_an_event_implementing_nothing(
        MarkerWireEvent wireEvent)
    {
        MatchesWireEvent(DamlTypeKind.Interface, new MarkerMatchScenario { Event = wireEvent })
            .Should().BeFalse();
    }

    [Fact]
    public void MatchesWireEvent_matches_an_interface_marker_Unassigned_event_the_participant_already_scoped()
    {
        MatchesWireEvent(
            DamlTypeKind.Interface,
            new MarkerMatchScenario
            {
                Event = MarkerWireEvent.Unassigned,
                TemplateId = MarkerMatchScenario.OtherTemplateId,
            })
            .Should().BeTrue();
    }

    [Fact]
    public void MatchesCreatedContract_matches_a_template_marker_by_module_and_entity_name_across_packages()
    {
        MatchesCreatedContract(DamlTypeKind.Template, CreatedContractWith(MarkerMatchScenario.MatchingTemplateId))
            .Should().BeTrue();
    }

    [Fact]
    public void MatchesCreatedContract_does_not_match_a_template_marker_on_a_different_entity_name()
    {
        MatchesCreatedContract(DamlTypeKind.Template, CreatedContractWith(MarkerMatchScenario.OtherTemplateId))
            .Should().BeFalse();
    }

    [Fact]
    public void MatchesCreatedContract_matches_an_interface_marker_through_an_implemented_interface_id()
    {
        MatchesCreatedContract(
            DamlTypeKind.Interface,
            CreatedContractWith(MarkerMatchScenario.MatchingTemplateId, MarkerMatchScenario.MatchingInterfaceId))
            .Should().BeTrue();
    }

    [Fact]
    public void MatchesCreatedContract_does_not_match_an_interface_marker_on_a_contract_implementing_nothing()
    {
        MatchesCreatedContract(DamlTypeKind.Interface, CreatedContractWith(MarkerMatchScenario.MatchingTemplateId))
            .Should().BeFalse();
    }

    [Fact]
    public void MatchesCreatedContract_does_not_match_an_interface_marker_through_a_different_interface_id()
    {
        MatchesCreatedContract(
            DamlTypeKind.Interface,
            CreatedContractWith(MarkerMatchScenario.MatchingTemplateId, MarkerMatchScenario.OtherInterfaceId))
            .Should().BeFalse();
    }

    private static CreatedContract CreatedContractWith(
        RuntimeIdentifier templateId, RuntimeIdentifier? implementedInterface = null) =>
        new("00holding", templateId, "{}")
        {
            InterfaceIds = implementedInterface is null ? [] : [implementedInterface],
        };
}
