// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Data;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Tests;

public class RestValueEncoderTests
{
    [Fact]
    public void ToWireValue_encodes_DamlBool_as_bool()
    {
        var wire = RestValueEncoder.ToWireValue(new DamlBool(true));

        wire.Bool.Should().BeTrue();
    }

    [Fact]
    public void ToWireValue_encodes_DamlInt64_as_a_decimal_string()
    {
        var wire = RestValueEncoder.ToWireValue(new DamlInt64(42));

        wire.Int64.Should().Be("42");
    }

    [Fact]
    public void ToWireValue_encodes_DamlNumeric_as_a_decimal_string()
    {
        var wire = RestValueEncoder.ToWireValue(new DamlNumeric(1.5m, 10));

        wire.Numeric.Should().Be("1.5");
    }

    [Fact]
    public void ToWireValue_encodes_DamlText_as_text()
    {
        var wire = RestValueEncoder.ToWireValue(new DamlText("hello"));

        wire.Text.Should().Be("hello");
    }

    [Fact]
    public void ToWireValue_encodes_DamlParty_as_party()
    {
        var wire = RestValueEncoder.ToWireValue(new DamlParty("alice::ns1"));

        wire.Party.Should().Be("alice::ns1");
    }

    [Fact]
    public void ToWireValue_encodes_DamlUnit_as_the_unit_marker()
    {
        var wire = RestValueEncoder.ToWireValue(DamlUnit.Instance);

        wire.AdditionalProperties.Should().ContainKey("unit");
    }

    [Fact]
    public void ToWireValue_encodes_an_empty_DamlOptional_with_no_value_set()
    {
        var wire = RestValueEncoder.ToWireValue(new DamlOptional(null));

        wire.Optional.Should().NotBeNull();
        wire.Optional!.Value.Should().BeNull();
    }

    [Fact]
    public void ToWireValue_encodes_a_populated_DamlOptional_with_the_inner_value()
    {
        var wire = RestValueEncoder.ToWireValue(new DamlOptional(new DamlText("some")));

        wire.Optional!.Value!.Text.Should().Be("some");
    }

    [Fact]
    public void ToWireValue_encodes_DamlList_elements_in_order()
    {
        var wire = RestValueEncoder.ToWireValue(new DamlList([new DamlInt64(1), new DamlInt64(2)]));

        wire.List!.Elements.Should().SatisfyRespectively(
            e => e.Int64.Should().Be("1"),
            e => e.Int64.Should().Be("2"));
    }

    [Fact]
    public void ToWireValue_encodes_DamlContractId_as_contract_id()
    {
        var wire = RestValueEncoder.ToWireValue(new Daml.Runtime.Contracts.DamlContractId("00cid", null));

        wire.ContractId.Should().Be("00cid");
    }

    [Fact]
    public void ToWireRecord_encodes_fields_with_labels_and_values_in_order()
    {
        var record = new DamlRecord(
            new RuntimeIdentifier("pkg", "Mod", "Ent"),
            [new DamlField("owner", new DamlParty("alice::ns1")), new DamlField("amount", new DamlInt64(10))]);

        var wire = RestValueEncoder.ToWireRecord(record);

        wire.Fields.Should().SatisfyRespectively(
            f => { f.Label.Should().Be("owner"); f.Value.Party.Should().Be("alice::ns1"); },
            f => { f.Label.Should().Be("amount"); f.Value.Int64.Should().Be("10"); });
    }

    [Fact]
    public void ToWireValue_round_trips_through_RestValueDecoder_for_a_nested_record()
    {
        var original = new DamlRecord(
            null,
            [new DamlField("tags", new DamlList([new DamlText("a"), new DamlText("b")]))]);

        var wire = RestValueEncoder.ToWireRecord(original);
        var decoded = RestValueDecoder.ToDamlRecord(wire);

        decoded.Should().BeEquivalentTo(original, options => options.PreferringRuntimeMemberTypes());
    }
}
