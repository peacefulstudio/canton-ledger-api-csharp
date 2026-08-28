// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Rest.Client.Tests;

public class RestSubscribeRequestBuilderTests
{
    private sealed record TemplateMarker : ITemplate
    {
        public static RuntimeIdentifier TemplateId { get; } = new("tmpl-pkg", "Sample.Token", "Holding");
        public static string PackageId => "tmpl-pkg";
        public static string PackageName => "token-impl";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => new(TemplateId, []);
    }

    private sealed record InterfaceMarker : IDamlInterface
    {
        public static RuntimeIdentifier InterfaceId { get; } = new("iface-pkg", "Sample.Token", "IHolding");
        public static string PackageId => "iface-pkg";
        public static string PackageName => "token-iface";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(InterfaceId, DamlTypeKind.Interface, PackageName);
        public DamlRecord ToRecord() => new(InterfaceId, []);
    }

    private static readonly Party Alice = new("party::alice");
    private static readonly Party Bob = new("party::bob");

    [Fact]
    public void BuildGetActiveContractsRequest_sets_the_active_at_offset_and_a_template_filter_per_party()
    {
        var submitter = (SubmitterInfo)Alice;

        var request = RestSubscribeRequestBuilder.BuildGetActiveContractsRequest<TemplateMarker>(submitter, 42L);

        request.ActiveAtOffset.Should().Be("42");
        var filters = request.EventFormat.FiltersByParty.Should().ContainKey("party::alice").WhoseValue;
        var identifierFilter = filters.Cumulative.Should().ContainSingle().Subject.IdentifierFilter;
        identifierFilter.Should().NotBeNull();
        identifierFilter!.TemplateFilter.Should().NotBeNull();
        identifierFilter.TemplateFilter!.TemplateId.PackageId.Should().Be("#token-impl");
        identifierFilter.TemplateFilter.TemplateId.ModuleName.Should().Be("Sample.Token");
        identifierFilter.TemplateFilter.TemplateId.EntityName.Should().Be("Holding");
        identifierFilter.InterfaceFilter.Should().BeNull();
    }

    [Fact]
    public void BuildGetActiveContractsRequest_sets_an_interface_filter_for_an_interface_marker()
    {
        var submitter = (SubmitterInfo)Alice;

        var request = RestSubscribeRequestBuilder.BuildGetActiveContractsRequest<InterfaceMarker>(submitter, 1L);

        var filters = request.EventFormat.FiltersByParty.Should().ContainKey("party::alice").WhoseValue;
        var identifierFilter = filters.Cumulative.Should().ContainSingle().Subject.IdentifierFilter;
        identifierFilter.Should().NotBeNull();
        identifierFilter!.InterfaceFilter.Should().NotBeNull();
        identifierFilter.InterfaceFilter!.InterfaceId.PackageId.Should().Be("#token-iface");
        identifierFilter.InterfaceFilter.IncludeInterfaceView.Should().BeTrue();
        identifierFilter.TemplateFilter.Should().BeNull();
    }

    [Fact]
    public void BuildGetActiveContractsRequest_scopes_the_filter_to_every_actAs_and_readAs_party()
    {
        var submitter = new SubmitterInfo(Alice, new HashSet<Party> { Bob });

        var request = RestSubscribeRequestBuilder.BuildGetActiveContractsRequest<TemplateMarker>(submitter, 1L);

        request.EventFormat.FiltersByParty.Keys.Should().BeEquivalentTo(["party::alice", "party::bob"]);
    }

    [Fact]
    public void BuildGetActiveContractsRequest_gives_each_party_an_independently_mutable_filter()
    {
        var submitter = new SubmitterInfo(Alice, new HashSet<Party> { Bob });

        var request = RestSubscribeRequestBuilder.BuildGetActiveContractsRequest<TemplateMarker>(submitter, 1L);

        var aliceFilter = request.EventFormat.FiltersByParty["party::alice"].Cumulative.Single();
        aliceFilter.IdentifierFilter = new Raw.IdentifierFilter { WildcardFilter = new Raw.WildcardFilter() };

        var bobFilter = request.EventFormat.FiltersByParty["party::bob"].Cumulative.Single();
        bobFilter.IdentifierFilter.Should().NotBeNull();
        bobFilter.IdentifierFilter!.WildcardFilter.Should().BeNull();
        bobFilter.IdentifierFilter.TemplateFilter.Should().NotBeNull();
    }

    [Fact]
    public void BuildGetUpdatesRequest_sets_begin_exclusive_and_end_inclusive_offsets()
    {
        var submitter = (SubmitterInfo)Alice;

        var request = RestSubscribeRequestBuilder.BuildGetUpdatesRequest<TemplateMarker>(
            submitter, beginExclusive: 5L, endInclusive: 99L, RestTransactionShape.AcsDelta);

        request.BeginExclusive.Should().Be("5");
        request.EndInclusive.Should().Be("99");
    }

    [Theory]
    [InlineData((int)RestTransactionShape.AcsDelta, Raw.TransactionFormatTransactionShape.TRANSACTION_SHAPE_ACS_DELTA)]
    [InlineData((int)RestTransactionShape.LedgerEffects, Raw.TransactionFormatTransactionShape.TRANSACTION_SHAPE_LEDGER_EFFECTS)]
    public void BuildGetUpdatesRequest_maps_the_transaction_shape_to_its_wire_value(
        int shapeOrdinal, Raw.TransactionFormatTransactionShape expectedWireValue)
    {
        var submitter = (SubmitterInfo)Alice;
        var shape = (RestTransactionShape)shapeOrdinal;

        var request = RestSubscribeRequestBuilder.BuildGetUpdatesRequest<TemplateMarker>(
            submitter, beginExclusive: 0L, endInclusive: 1L, shape);

        request.UpdateFormat.IncludeTransactions.TransactionShape.Should().Be(expectedWireValue);
    }

    [Fact]
    public void BuildGetUpdatesRequest_includes_reassignments_with_an_equivalent_event_format()
    {
        var submitter = (SubmitterInfo)Alice;

        var request = RestSubscribeRequestBuilder.BuildGetUpdatesRequest<TemplateMarker>(
            submitter, beginExclusive: 0L, endInclusive: 1L, RestTransactionShape.AcsDelta);

        request.UpdateFormat.IncludeReassignments.FiltersByParty.Should().ContainKey("party::alice");
    }

    [Fact]
    public void BuildGetUpdatesRequest_gives_reassignments_an_EventFormat_independent_of_the_transaction_one()
    {
        var submitter = new SubmitterInfo(Alice, new HashSet<Party> { Bob });

        var request = RestSubscribeRequestBuilder.BuildGetUpdatesRequest<TemplateMarker>(
            submitter, beginExclusive: 0L, endInclusive: 1L, RestTransactionShape.AcsDelta);

        var transactions = request.UpdateFormat.IncludeTransactions.EventFormat;
        transactions.Verbose = false;
        transactions.FiltersByParty.Remove("party::bob");
        transactions.FiltersByParty["party::alice"].Cumulative.Add(new Raw.CumulativeFilter
        {
            IdentifierFilter = new Raw.IdentifierFilter { WildcardFilter = new Raw.WildcardFilter() },
        });

        var reassignments = request.UpdateFormat.IncludeReassignments;
        reassignments.Verbose.Should().BeTrue();
        reassignments.FiltersByParty.Keys.Should().BeEquivalentTo(["party::alice", "party::bob"]);
        reassignments.FiltersByParty["party::alice"].Cumulative.Should().ContainSingle();
    }
}
