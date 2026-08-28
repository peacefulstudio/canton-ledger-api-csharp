// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Rest.Client.Raw;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class CumulativeFilterSerializationTests
{
    private const string FlatIdentifier = "#richtypes:RichTypes:Marker";

    private static Identifier MarkerIdentifier() =>
        new() { PackageId = "#richtypes", ModuleName = "RichTypes", EntityName = "Marker" };

    [Fact]
    public void CumulativeFilter_nests_a_template_filter_under_identifierFilter()
    {
        var filter = new CumulativeFilter
        {
            IdentifierFilter = new IdentifierFilter
            {
                TemplateFilter = new TemplateFilter { TemplateId = MarkerIdentifier() },
            },
        };

        var json = JsonSerializer.Serialize(filter, RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        var identifierFilter = document.RootElement.GetProperty("identifierFilter");
        identifierFilter.EnumerateObject().Select(property => property.Name).Should().Equal("TemplateFilter");
        identifierFilter.GetProperty("TemplateFilter").GetProperty("value").GetProperty("templateId")
            .GetString().Should().Be(FlatIdentifier);
    }

    [Fact]
    public void CumulativeFilter_nests_an_interface_filter_under_identifierFilter()
    {
        var filter = new CumulativeFilter
        {
            IdentifierFilter = new IdentifierFilter
            {
                InterfaceFilter = new InterfaceFilter
                {
                    InterfaceId = MarkerIdentifier(),
                    IncludeInterfaceView = true,
                },
            },
        };

        var json = JsonSerializer.Serialize(filter, RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        var value = document.RootElement.GetProperty("identifierFilter")
            .GetProperty("InterfaceFilter").GetProperty("value");
        value.GetProperty("interfaceId").GetString().Should().Be(FlatIdentifier);
        value.GetProperty("includeInterfaceView").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void CumulativeFilter_never_writes_a_flat_templateFilter_sibling()
    {
        var filter = new CumulativeFilter
        {
            IdentifierFilter = new IdentifierFilter
            {
                TemplateFilter = new TemplateFilter { TemplateId = MarkerIdentifier() },
            },
        };

        var json = JsonSerializer.Serialize(filter, RestRefitSettings.SerializerOptions);

        json.Should().NotContain("\"templateFilter\"");
    }

    [Fact]
    public void IdentifierFilter_reads_an_unrecognised_arm_into_the_extension_bag()
    {
        var identifierFilter = JsonSerializer.Deserialize<IdentifierFilter>(
            "{\"Empty\":{}}", RestRefitSettings.SerializerOptions);

        identifierFilter.Should().NotBeNull();
        identifierFilter.AdditionalProperties.Should().ContainKey("Empty");
    }

    [Fact]
    public void IdentifierFilter_rejects_a_declared_arm_that_arrives_without_its_value_level()
    {
        var act = () => JsonSerializer.Deserialize<IdentifierFilter>(
            """{"TemplateFilter":{"templateId":"#richtypes:RichTypes:Marker"}}""",
            RestRefitSettings.SerializerOptions);

        act.Should().Throw<JsonException>().WithMessage("*TemplateFilter*value*");
    }

    [Fact]
    public void IdentifierFilter_rejects_two_arms_set_at_once()
    {
        var identifierFilter = new IdentifierFilter
        {
            TemplateFilter = new TemplateFilter { TemplateId = MarkerIdentifier() },
            WildcardFilter = new WildcardFilter(),
        };

        var act = () => JsonSerializer.Serialize(identifierFilter, RestRefitSettings.SerializerOptions);

        act.Should().Throw<JsonException>().WithMessage("*exactly one*");
    }

    [Fact]
    public void IdentifierFilter_rejects_a_declared_arm_set_alongside_an_arm_it_carried_in()
    {
        var identifierFilter = JsonSerializer.Deserialize<IdentifierFilter>(
            """{"Empty":{}}""", RestRefitSettings.SerializerOptions);
        identifierFilter.Should().NotBeNull();
        identifierFilter.TemplateFilter = new TemplateFilter { TemplateId = MarkerIdentifier() };

        var act = () => JsonSerializer.Serialize(identifierFilter, RestRefitSettings.SerializerOptions);

        act.Should().Throw<JsonException>().WithMessage("*exactly one*");
    }

    [Fact]
    public void IdentifierFilter_writes_back_the_lone_arm_it_carried_in()
    {
        var identifierFilter = JsonSerializer.Deserialize<IdentifierFilter>(
            """{"Empty":{}}""", RestRefitSettings.SerializerOptions);
        identifierFilter.Should().NotBeNull();

        var json = JsonSerializer.Serialize(identifierFilter, RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        document.RootElement.EnumerateObject().Select(property => property.Name).Should().Equal("Empty");
    }

    [Fact]
    public void IdentifierFilter_rejects_an_arm_set_being_empty()
    {
        var act = () => JsonSerializer.Serialize(
            new IdentifierFilter(), RestRefitSettings.SerializerOptions);

        act.Should().Throw<JsonException>().WithMessage("*exactly one*");
    }
}
