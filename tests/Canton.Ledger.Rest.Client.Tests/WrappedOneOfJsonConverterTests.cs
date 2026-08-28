// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public class WrappedOneOfJsonConverterTests
{
    private static readonly JsonSerializerOptions MixedArmOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new WrappedOneOfJsonConverterFactory(
                new WrappedOneOf(typeof(MixedArmWrapper), "Bare", "BareCarryingItsOwnValueField")),
        },
    };

    [Fact]
    public void Read_decodes_a_bare_arm_that_carries_no_value_level()
    {
        var wrapper = JsonSerializer.Deserialize<MixedArmWrapper>(
            """{"Bare":{"offset":"9728"}}""", MixedArmOptions);

        wrapper.Should().NotBeNull();
        wrapper.Bare.Should().NotBeNull();
        wrapper.Bare.Offset.Should().Be("9728");
        wrapper.Wrapped.Should().BeNull();
    }

    [Fact]
    public void Read_decodes_a_wrapped_arm_of_the_same_type_through_its_value_level()
    {
        var wrapper = JsonSerializer.Deserialize<MixedArmWrapper>(
            """{"Wrapped":{"value":{"offset":"9706"}}}""", MixedArmOptions);

        wrapper.Should().NotBeNull();
        wrapper.Wrapped.Should().NotBeNull();
        wrapper.Wrapped.Offset.Should().Be("9706");
        wrapper.Bare.Should().BeNull();
    }

    [Fact]
    public void Read_rejects_a_wrapped_arm_that_arrives_without_its_value_level()
    {
        var act = () => JsonSerializer.Deserialize<MixedArmWrapper>(
            """{"Wrapped":{"offset":"9706"}}""", MixedArmOptions);

        act.Should().Throw<JsonException>().WithMessage("*Wrapped*value*");
    }

    [Fact]
    public void Read_rejects_a_bare_arm_that_arrives_inside_a_value_level()
    {
        var act = () => JsonSerializer.Deserialize<MixedArmWrapper>(
            """{"Bare":{"value":{"offset":"9728"}}}""", MixedArmOptions);

        act.Should().Throw<JsonException>().WithMessage("*Bare*value*");
    }

    [Fact]
    public void Read_accepts_a_bare_arm_whose_payload_declares_a_value_field_of_its_own()
    {
        var wrapper = JsonSerializer.Deserialize<MixedArmWrapper>(
            """{"BareCarryingItsOwnValueField":{"value":"held","offset":"9728"}}""", MixedArmOptions);

        wrapper.Should().NotBeNull();
        wrapper.BareCarryingItsOwnValueField.Should().NotBeNull();
        wrapper.BareCarryingItsOwnValueField.Value.Should().Be("held");
        wrapper.BareCarryingItsOwnValueField.Offset.Should().Be("9728");
    }

    [Fact]
    public void Write_emits_a_bare_arm_without_a_value_level()
    {
        var wrapper = new MixedArmWrapper { Bare = new ArmPayload { Offset = "9728" } };

        var json = JsonSerializer.Serialize(wrapper, MixedArmOptions);

        json.Should().Be("""{"Bare":{"offset":"9728"}}""");
    }

    [Fact]
    public void Write_wraps_an_arm_that_was_not_declared_bare()
    {
        var wrapper = new MixedArmWrapper { Wrapped = new ArmPayload { Offset = "9706" } };

        var json = JsonSerializer.Serialize(wrapper, MixedArmOptions);

        json.Should().Be("""{"Wrapped":{"value":{"offset":"9706"}}}""");
    }

    [Fact]
    public void Read_carries_an_undeclared_arm_into_the_extension_bag()
    {
        var wrapper = JsonSerializer.Deserialize<MixedArmWrapper>(
            """{"Empty":{}}""", MixedArmOptions);

        wrapper.Should().NotBeNull();
        wrapper.AdditionalProperties.Should().ContainKey("Empty");
    }
}

internal sealed class MixedArmWrapper
{
    [JsonPropertyName("Wrapped")]
    public ArmPayload? Wrapped { get; set; }

    [JsonPropertyName("Bare")]
    public ArmPayload? Bare { get; set; }

    [JsonPropertyName("BareCarryingItsOwnValueField")]
    public ValueFieldPayload? BareCarryingItsOwnValueField { get; set; }

    [JsonExtensionData]
    public IDictionary<string, object>? AdditionalProperties { get; set; }
}

internal sealed class ArmPayload
{
    [JsonPropertyName("offset")]
    public string? Offset { get; set; }
}

internal sealed class ValueFieldPayload
{
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("offset")]
    public string? Offset { get; set; }
}
