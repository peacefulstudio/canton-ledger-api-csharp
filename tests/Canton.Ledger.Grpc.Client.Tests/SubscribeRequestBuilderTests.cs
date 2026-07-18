// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Xunit;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Grpc.Client.Tests;

public class SubscribeRequestBuilderTests
{
    private static readonly ProtoIdentifier TemplateId = new()
    {
        PackageId = "pkg",
        ModuleName = "Module",
        EntityName = "Template",
    };

    [Fact]
    public void BuildGetUpdatesRequest_includes_readAs_party_in_FiltersByParty_even_with_single_actAs()
    {
        var submitter = new RuntimeCommands.SubmitterInfo(
            new HashSet<Party> { (Party)"alice" },
            new HashSet<Party> { (Party)"observer" });

        var request = SubscribeRequestBuilder.BuildGetUpdatesRequest(submitter, TemplateId, fromOffset: null, toOffset: null);

        var filtersByParty = request.UpdateFormat.IncludeTransactions.EventFormat.FiltersByParty;
        filtersByParty.Keys.Should().BeEquivalentTo(["alice", "observer"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildGetUpdatesRequest_uses_the_AcsDelta_shape_for_both_template_and_interface_markers(bool isInterface)
    {
        var submitter = new RuntimeCommands.SubmitterInfo(
            new HashSet<Party> { (Party)"alice" },
            new HashSet<Party>());

        var request = SubscribeRequestBuilder.BuildGetUpdatesRequest(
            submitter, TemplateId, fromOffset: null, toOffset: null, isInterface: isInterface);

        request.UpdateFormat.IncludeTransactions.TransactionShape.Should().Be(TransactionShape.AcsDelta);
    }

    [Fact]
    public void BuildGetUpdatesRequest_sets_IncludeReassignments_mirroring_the_transaction_template_filter()
    {
        var submitter = new RuntimeCommands.SubmitterInfo(
            new HashSet<Party> { (Party)"alice" },
            new HashSet<Party> { (Party)"observer" });

        var request = SubscribeRequestBuilder.BuildGetUpdatesRequest(submitter, TemplateId, fromOffset: null, toOffset: null);

        var reassignments = request.UpdateFormat.IncludeReassignments;
        reassignments.Should().NotBeNull();
        reassignments.FiltersByParty.Keys.Should().BeEquivalentTo(
            request.UpdateFormat.IncludeTransactions.EventFormat.FiltersByParty.Keys);
        reassignments.FiltersByParty["alice"].Cumulative[0].TemplateFilter.TemplateId.EntityName
            .Should().Be("Template");
    }

    [Fact]
    public void BuildGetUpdatesRequest_sets_IncludeReassignments_with_an_interface_filter_for_interface_markers()
    {
        var submitter = new RuntimeCommands.SubmitterInfo(
            new HashSet<Party> { (Party)"alice" },
            new HashSet<Party>());

        var request = SubscribeRequestBuilder.BuildGetUpdatesRequest(
            submitter, TemplateId, fromOffset: null, toOffset: null, isInterface: true);

        var cumulative = request.UpdateFormat.IncludeReassignments.FiltersByParty["alice"].Cumulative[0];
        cumulative.InterfaceFilter.InterfaceId.EntityName.Should().Be("Template");
        cumulative.InterfaceFilter.IncludeInterfaceView.Should().BeTrue();
    }

    [Fact]
    public void BuildGetUpdatesRequest_passes_fromOffset_through_as_BeginExclusive()
    {
        var submitter = new RuntimeCommands.SubmitterInfo(
            new HashSet<Party> { (Party)"alice" },
            new HashSet<Party>());

        var request = SubscribeRequestBuilder.BuildGetUpdatesRequest(submitter, TemplateId, fromOffset: 42L, toOffset: null);

        request.BeginExclusive.Should().Be(42L);
    }

    [Fact]
    public void BuildGetUpdatesRequest_sets_EndInclusive_when_toOffset_supplied()
    {
        var submitter = new RuntimeCommands.SubmitterInfo(
            new HashSet<Party> { (Party)"alice" },
            new HashSet<Party>());

        var request = SubscribeRequestBuilder.BuildGetUpdatesRequest(submitter, TemplateId, fromOffset: 10L, toOffset: 20L);

        request.HasEndInclusive.Should().BeTrue();
        request.EndInclusive.Should().Be(20L);
    }

    [Fact]
    public void BuildGetUpdatesRequest_leaves_EndInclusive_unset_when_toOffset_is_null()
    {
        var submitter = new RuntimeCommands.SubmitterInfo(
            new HashSet<Party> { (Party)"alice" },
            new HashSet<Party>());

        var request = SubscribeRequestBuilder.BuildGetUpdatesRequest(submitter, TemplateId, fromOffset: 10L, toOffset: null);

        request.HasEndInclusive.Should().BeFalse();
    }

    [Fact]
    public void BuildGetUpdatesRequest_defaults_BeginExclusive_to_zero_when_fromOffset_is_null()
    {
        var submitter = new RuntimeCommands.SubmitterInfo(
            new HashSet<Party> { (Party)"alice" },
            new HashSet<Party>());

        var request = SubscribeRequestBuilder.BuildGetUpdatesRequest(submitter, TemplateId, fromOffset: null, toOffset: null);

        request.BeginExclusive.Should().Be(0L);
    }

    [Fact]
    public void BuildGetActiveContractsRequest_passes_activeAtOffset_through()
    {
        var submitter = new RuntimeCommands.SubmitterInfo(
            new HashSet<Party> { (Party)"alice" },
            new HashSet<Party>());

        var request = SubscribeRequestBuilder.BuildGetActiveContractsRequest(submitter, TemplateId, activeAtOffset: 999L);

        request.ActiveAtOffset.Should().Be(999L);
    }

    [Fact]
    public void BuildGetActiveContractsRequest_carries_package_name_reference_into_template_filter()
    {
        var submitter = new RuntimeCommands.SubmitterInfo(
            new HashSet<Party> { (Party)"alice" },
            new HashSet<Party>());
        var packageNameTemplateId = new ProtoIdentifier
        {
            PackageId = "#richtypes",
            ModuleName = "RichTypes",
            EntityName = "RichRecord",
        };

        var request = SubscribeRequestBuilder.BuildGetActiveContractsRequest(submitter, packageNameTemplateId, activeAtOffset: 0L);

        request.EventFormat.FiltersByParty["alice"].Cumulative[0].TemplateFilter.TemplateId.PackageId
            .Should().Be("#richtypes");
    }

    [Fact]
    public void BuildTransactionFormat_covers_every_actAs_and_readAs_party_with_a_wildcard_filter()
    {
        var submitter = new RuntimeCommands.SubmitterInfo(
            new HashSet<Party> { (Party)"alice" },
            new HashSet<Party> { (Party)"observer" });

        var transactionFormat = SubscribeRequestBuilder.BuildTransactionFormat(submitter);

        transactionFormat.TransactionShape.Should().Be(TransactionShape.LedgerEffects);
        var filtersByParty = transactionFormat.EventFormat.FiltersByParty;
        filtersByParty.Keys.Should().BeEquivalentTo(["alice", "observer"]);
        filtersByParty.Values.Should().OnlyContain(filters => filters.Cumulative.Count == 0);
    }
}
