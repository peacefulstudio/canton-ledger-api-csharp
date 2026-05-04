// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Daml.Runtime.Data;
using FluentAssertions;
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

        var request = SubscribeRequestBuilder.BuildGetUpdatesRequest(submitter, TemplateId, fromOffset: null);

        var filtersByParty = request.UpdateFormat.IncludeTransactions.EventFormat.FiltersByParty;
        filtersByParty.Keys.Should().BeEquivalentTo(["alice", "observer"]);
    }

    [Fact]
    public void BuildGetUpdatesRequest_passes_fromOffset_through_as_BeginExclusive()
    {
        var submitter = new RuntimeCommands.SubmitterInfo(
            new HashSet<Party> { (Party)"alice" },
            new HashSet<Party>());

        var request = SubscribeRequestBuilder.BuildGetUpdatesRequest(submitter, TemplateId, fromOffset: 42L);

        request.BeginExclusive.Should().Be(42L);
    }

    [Fact]
    public void BuildGetUpdatesRequest_defaults_BeginExclusive_to_zero_when_fromOffset_is_null()
    {
        var submitter = new RuntimeCommands.SubmitterInfo(
            new HashSet<Party> { (Party)"alice" },
            new HashSet<Party>());

        var request = SubscribeRequestBuilder.BuildGetUpdatesRequest(submitter, TemplateId, fromOffset: null);

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
}
