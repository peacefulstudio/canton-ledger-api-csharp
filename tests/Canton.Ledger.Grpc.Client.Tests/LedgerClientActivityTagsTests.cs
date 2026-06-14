// Copyright 2026 Peaceful Studio OÜ

using FluentAssertions;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public class LedgerClientActivityTagsTests
{
    [Fact]
    public void Choice_pins_the_canonical_wire_name() =>
        LedgerClientActivityTags.Choice.Should().Be("choice");

    [Fact]
    public void ContractId_pins_the_canonical_wire_name() =>
        LedgerClientActivityTags.ContractId.Should().Be("contractId");

    [Fact]
    public void TemplateType_pins_the_canonical_wire_name() =>
        LedgerClientActivityTags.TemplateType.Should().Be("template.type");

    [Fact]
    public void FromOffset_pins_the_canonical_wire_name() =>
        LedgerClientActivityTags.FromOffset.Should().Be("fromOffset");

    [Fact]
    public void Offset_pins_the_canonical_wire_name() =>
        LedgerClientActivityTags.Offset.Should().Be("offset");

    [Fact]
    public void SubmitterActAs_pins_the_canonical_wire_name() =>
        LedgerClientActivityTags.SubmitterActAs.Should().Be("submitter.actAs");

    [Fact]
    public void SubmitterReadAs_pins_the_canonical_wire_name() =>
        LedgerClientActivityTags.SubmitterReadAs.Should().Be("submitter.readAs");
}
