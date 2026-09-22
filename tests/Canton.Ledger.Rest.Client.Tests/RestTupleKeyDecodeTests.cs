// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.ContractKeys;
using Daml.Runtime.Data;
using Xunit;
using WireCreatedEvent = Canton.Ledger.Rest.Client.Raw.CreatedEvent;
using WireIdentifier = Canton.Ledger.Rest.Client.Raw.Identifier;
using WireValue = Canton.Ledger.Rest.Client.Raw.Value;

namespace Canton.Ledger.Rest.Client.Tests;

/// <summary>
/// Contract keys shaped as a <c>Tuple2</c> on the shared <c>ContractKeys</c> templates, read from a
/// created event through the key reader the template's generated descriptor carries.
/// </summary>
public sealed class RestTupleKeyDecodeTests
{
    private static WireCreatedEvent CreatedWithKey(string templateIdText, string keyJson)
    {
        var parts = templateIdText.Split(':');
        return new WireCreatedEvent
        {
            NodeId = 0,
            Offset = "1",
            ContractId = "00keyed",
            TemplateId = new WireIdentifier { PackageId = parts[0], ModuleName = parts[1], EntityName = parts[2] },
            ContractKey = JsonSerializer.Deserialize<WireValue>(keyJson, RestRefitSettings.SerializerOptions)!,
        };
    }

    [Fact]
    public void A_Tuple2_key_of_a_party_and_text_decodes_to_a_record_holding_both_components()
    {
        var created = CreatedWithKey(
            "1435b1fe66cf84bdbf8ff0e9f0fd942e662bb6013aa1dbf33da20fc2a14cdb5d:ContractKeys:Membership",
            """{"_1": "alice::ns1", "_2": "gold"}""");

        var key = RestPayloadDecoder.ContractKeyOf(created, Membership.TemplateId);

        var tuple = key!.Value.Should().BeOfType<DamlRecord>().Subject;
        tuple.GetRequiredField("_1").Should().BeOfType<DamlParty>().Which.Value.Should().Be("alice::ns1");
        tuple.GetRequiredField("_2").Should().BeOfType<DamlText>().Which.Value.Should().Be("gold");
    }

    [Fact]
    public void A_Tuple2_key_whose_second_component_is_an_absent_Optional_decodes_it_as_an_empty_DamlOptional()
    {
        var created = CreatedWithKey(
            "1435b1fe66cf84bdbf8ff0e9f0fd942e662bb6013aa1dbf33da20fc2a14cdb5d:ContractKeys:Enrollment",
            """{"_1": "alice::ns1", "_2": null}""");

        var key = RestPayloadDecoder.ContractKeyOf(created, Enrollment.TemplateId);

        var tuple = key!.Value.Should().BeOfType<DamlRecord>().Subject;
        tuple.GetRequiredField("_1").Should().BeOfType<DamlParty>().Which.Value.Should().Be("alice::ns1");
        tuple.GetRequiredField("_2").Should().BeOfType<DamlOptional>().Which.Value.Should().BeNull();
    }
}
