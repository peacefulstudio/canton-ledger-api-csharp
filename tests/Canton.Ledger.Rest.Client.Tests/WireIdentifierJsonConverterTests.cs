// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Rest.Client.Raw;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Tests;

public class WireIdentifierJsonConverterTests
{
    [Fact]
    public void Write_writes_the_colon_separated_form()
    {
        var identifier = new Identifier { PackageId = "3557ff", ModuleName = "RichTypes", EntityName = "Marker" };

        var json = JsonSerializer.Serialize(identifier, RestRefitSettings.SerializerOptions);

        json.Should().Be("\"3557ff:RichTypes:Marker\"");
    }

    [Fact]
    public void Write_writes_the_templateId_of_a_CreateCommand_as_a_flat_string()
    {
        var command = new CreateCommand
        {
            TemplateId = new Identifier { PackageId = "3557ff", ModuleName = "RichTypes", EntityName = "Marker" },
        };

        var json = JsonSerializer.Serialize(command, RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("templateId").GetString().Should().Be("3557ff:RichTypes:Marker");
    }

    [Fact]
    public void Read_parses_the_colon_separated_form()
    {
        var identifier = Deserialize("\"3557ff:RichTypes:Marker\"");

        identifier.Should().NotBeNull();
        identifier.PackageId.Should().Be("3557ff");
        identifier.ModuleName.Should().Be("RichTypes");
        identifier.EntityName.Should().Be("Marker");
    }

    [Fact]
    public void Read_preserves_the_package_name_reference_form()
    {
        Deserialize("\"#my-package:RichTypes:Marker\"").PackageId.Should().Be("#my-package");
    }

    [Fact]
    public void Read_preserves_dotted_module_names()
    {
        var identifier = Deserialize("\"pkg:Some.Nested.Module:Entity\"");

        identifier.ModuleName.Should().Be("Some.Nested.Module");
        identifier.EntityName.Should().Be("Entity");
    }

    [Fact]
    public void Read_still_accepts_the_structured_form_our_specification_declares()
    {
        var identifier = Deserialize(
            """{"packageId":"3557ff","moduleName":"RichTypes","entityName":"Marker"}""");

        identifier.PackageId.Should().Be("3557ff");
        identifier.ModuleName.Should().Be("RichTypes");
        identifier.EntityName.Should().Be("Marker");
    }

    [Fact]
    public void Read_yields_no_identifier_for_a_JSON_null()
    {
        JsonSerializer.Deserialize<Identifier>("null", RestRefitSettings.SerializerOptions).Should().BeNull();
    }

    [Fact]
    public void Read_yields_no_identifier_for_the_null_InterfaceId_of_a_non_interface_ExercisedEvent()
    {
        var exercised = JsonSerializer.Deserialize<ExercisedEvent>(
            """{"templateId":"3557ff:RichTypes:Marker","interfaceId":null,"choice":"Ping"}""",
            RestRefitSettings.SerializerOptions)!;

        exercised.InterfaceId.Should().BeNull();
        exercised.TemplateId.EntityName.Should().Be("Marker");
    }

    [Theory]
    [InlineData("\"pkg:Module\"")]
    [InlineData("\"pkg:Module:Entity:extra\"")]
    [InlineData("\"\"")]
    [InlineData("\"pkg::Entity\"")]
    [InlineData("42")]
    [InlineData("[\"pkg\",\"Module\",\"Entity\"]")]
    public void Read_rejects_malformed_identifiers_descriptively(string json)
    {
        var read = () => Deserialize(json);

        read.Should().Throw<JsonException>()
            .WithMessage("*packageId:moduleName:entityName*");
    }

    private static Identifier Deserialize(string json) =>
        JsonSerializer.Deserialize<Identifier>(json, RestRefitSettings.SerializerOptions)!;
}
