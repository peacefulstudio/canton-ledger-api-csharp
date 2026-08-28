// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Rest.Client.Integration.Tests;

/// <summary>
/// LocalNet conformance coverage for the one structural fact <c>ReassignmentEvent</c> is decoded by:
/// the participant serves a <c>JsReassignmentEvent</c> whose two arms are wrapped differently.
/// <c>JsAssignmentEvent</c> is hand-modelled and carries its payload bare under the arm key, while
/// <c>JsUnassignedEvent</c> is a scalapb passthrough that keeps the extra <c>value</c> level, so
/// <c>RestRefitSettings</c> registers the oneof with <c>JsAssignmentEvent</c> declared bare and every
/// other arm wrapped.
/// <para>
/// Getting that asymmetry wrong is invisible at run time. An arm key that does not bind leaves its
/// payload in the extension bag, both arms read as null, and <c>ContractStreamProjector</c> reports
/// <c>Unclassified(UnclassifiedKind.Unknown, "empty-reassignment-event")</c> — no exception and no
/// log, with the reassignment simply gone. Unit tests over hand-written literals cannot catch it
/// either, because a literal written to the wrong shape agrees with a decoder written to the wrong
/// shape.
/// </para>
/// <para>
/// So the assertion is made against the schema the participant serves for itself at
/// <c>/docs/openapi</c>, fetched live on every run. That document is emitted from the participant's
/// own endpoint definitions and is therefore authoritative about the wire in a way our vendored,
/// protobuf-derived <c>spec/openapi.yaml</c> is not. Comparing a committed copy against another
/// committed copy would prove nothing.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
public class RestReassignmentEventShapeConformanceTests(ITestOutputHelper output)
{
    private static readonly string[] ArmKeys = ["JsAssignmentEvent", "JsUnassignedEvent"];

    private static readonly string[] BareAssignmentProperties =
        ["source", "target", "reassignmentId", "submitter", "reassignmentCounter", "createdEvent"];

    private static readonly string[] WrappingValueLevel = ["value"];

    [Fact]
    public async Task JsReassignmentEvent_offers_exactly_the_assignment_and_unassignment_arms()
    {
        var served = await ServedSchemaAsync(TestContext.Current.CancellationToken);

        served.OneOfArmKeysOf("JsReassignmentEvent").Should().Equal(
            ArmKeys,
            "ReassignmentEvent binds a served reassignment by exactly these two arm keys. An arm that is "
            + "renamed, dropped or added leaves its payload unbound, which surfaces as a silently "
            + "Unclassified event rather than as a decode error, so the arm set has to be pinned here");
    }

    [Fact]
    public async Task JsAssignmentEvent_carries_its_payload_bare_under_the_arm_key()
    {
        var served = await ServedSchemaAsync(TestContext.Current.CancellationToken);

        served.RequiredPropertiesOf("JsAssignmentEvent").Should().Equal(
            BareAssignmentProperties,
            "RestRefitSettings declares the JsAssignmentEvent arm bare, meaning the assignment's own "
            + "properties sit directly under the arm key. A 'value' level appearing here would make that "
            + "declaration wrong and every assignment event would decode as null");
    }

    [Fact]
    public async Task JsUnassignedEvent_keeps_its_payload_behind_a_value_level()
    {
        var served = await ServedSchemaAsync(TestContext.Current.CancellationToken);

        served.RequiredPropertiesOf("JsUnassignedEvent").Should().Equal(
            WrappingValueLevel,
            "the JsUnassignedEvent arm is a scalapb passthrough, so unlike its bare sibling it keeps the "
            + "wrapping 'value' level. Declaring this arm bare too would make every unassigned event "
            + "decode as null");

        served.ReferenceTargetOf("JsUnassignedEvent", "value").Should().Be(
            "#/components/schemas/UnassignedEvent",
            "the value level wraps the UnassignedEvent payload itself; a different target would mean the "
            + "arm no longer carries what ReassignmentEvent projects an unassignment from");
    }

    private async Task<ServedOpenApiDocument> ServedSchemaAsync(CancellationToken cancellationToken)
    {
        await using var lane = await RestConformanceLane.OpenAsync(cancellationToken);
        using var client = lane.CreateWireLevelClient();

        var served = await ServedOpenApiDocument.FetchAsync(client, cancellationToken);
        output.WriteLine($"Participant serves its JSON Ledger API schema as version {served.CantonVersion}.");
        return served;
    }
}
