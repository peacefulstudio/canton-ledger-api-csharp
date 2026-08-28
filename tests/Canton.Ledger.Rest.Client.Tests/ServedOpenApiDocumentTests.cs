// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Rest.Client.Integration.Tests;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

/// <summary>
/// Coverage for the reader behind the reassignment wire-shape conformance test. That test asserts
/// against a document fetched live, so a reader that quietly mis-answers would turn a real wire
/// change into a green run. The excerpt below is copied verbatim out of a served document, keeping
/// the two traits the reader has to survive — sequences written at their parent key's own indent,
/// and a folded description sitting between a schema's title and its body.
/// </summary>
public class ServedOpenApiDocumentTests
{
    private const string ServedExcerpt =
        """
        openapi: 3.0.3
        info:
          title: JSON Ledger API HTTP endpoints
          version: 3.5.11
        components:
          schemas:
            JsAssignmentEvent:
              title: JsAssignmentEvent
              type: object
              required:
              - source
              - createdEvent
              properties:
                source:
                  type: string
                createdEvent:
                  $ref: '#/components/schemas/CreatedEvent'
            JsReassignmentEvent:
              title: JsReassignmentEvent
              oneOf:
              - type: object
                required:
                - JsAssignmentEvent
                properties:
                  JsAssignmentEvent:
                    $ref: '#/components/schemas/JsAssignmentEvent'
              - type: object
                required:
                - JsUnassignedEvent
                properties:
                  JsUnassignedEvent:
                    $ref: '#/components/schemas/JsUnassignedEvent'
            JsUnassignedEvent:
              title: JsUnassignedEvent
              description: Records that a contract has been unassigned, and it becomes unusable
                on the source synchronizer
              type: object
              required:
              - value
              properties:
                value:
                  $ref: '#/components/schemas/UnassignedEvent'
        """;

    private static ServedOpenApiDocument Excerpt() => ServedOpenApiDocument.Parse(ServedExcerpt);

    [Fact]
    public void Parse_reads_the_canton_version_the_document_declares()
        => Excerpt().CantonVersion.Should().Be("3.5.11");

    [Fact]
    public void OneOfArmKeysOf_reads_one_arm_key_per_oneOf_element_in_order()
        => Excerpt().OneOfArmKeysOf("JsReassignmentEvent").Should()
            .Equal("JsAssignmentEvent", "JsUnassignedEvent");

    [Fact]
    public void RequiredPropertiesOf_reads_a_sequence_written_at_its_parent_keys_indent()
        => Excerpt().RequiredPropertiesOf("JsAssignmentEvent").Should().Equal("source", "createdEvent");

    [Fact]
    public void RequiredPropertiesOf_is_not_confused_by_a_folded_description_above_it()
        => Excerpt().RequiredPropertiesOf("JsUnassignedEvent").Should().Equal("value");

    [Fact]
    public void ReferenceTargetOf_reads_a_quoted_ref_out_of_a_named_property()
        => Excerpt().ReferenceTargetOf("JsUnassignedEvent", "value").Should()
            .Be("#/components/schemas/UnassignedEvent");

    [Fact]
    public void RequiredPropertiesOf_throws_naming_the_schema_it_could_not_find()
    {
        var act = () => Excerpt().RequiredPropertiesOf("JsAbsentEvent");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*JsAbsentEvent*JsReassignmentEvent*");
    }
}
