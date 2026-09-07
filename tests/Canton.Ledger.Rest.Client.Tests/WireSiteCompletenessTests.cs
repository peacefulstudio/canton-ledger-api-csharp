// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Canton.Ledger.Rest.Client.Raw;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class WireSiteCompletenessTests
{
    private const string RawNamespace = "Canton.Ledger.Rest.Client.Raw";

    private const string Proto3DurationPattern = @"^-?(?:0|[1-9][0-9]{0,11})(?:\.[0-9]{1,9})?s$";

    private static readonly FrozenSet<string> TypesEitherWireTableClaims =
        new[]
        {
            "ActiveContract",
            "ArchivedEvent",
            "AssignedEvent",
            "Commands",
            "Completion",
            "CostEstimation",
            "CreatedEvent",
            "ExecuteSubmissionAndWaitResponse",
            "ExercisedEvent",
            "GetActiveContractsPageResponse",
            "GetLatestPrunedOffsetsResponse",
            "GetLedgerEndResponse",
            "GetUpdatesPageResponse",
            "OffsetCheckpoint",
            "OffsetCheckpointFeature",
            "Reassignment",
            "SubmitAndWaitResponse",
            "TopologyTransaction",
            "Transaction",
            "UnassignedEvent",
        }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> SitesCarryingTextRatherThanANumberOrADuration =
        new[]
        {
            "ActiveContract.synchronizerId",
            "ArchivedEvent.contractId",
            "ArchivedEvent.packageName",
            "AssignedEvent.reassignmentId",
            "AssignedEvent.source",
            "AssignedEvent.submitter",
            "AssignedEvent.target",
            "Commands.commandId",
            "Commands.submissionId",
            "Commands.synchronizerId",
            "Commands.userId",
            "Commands.workflowId",
            "Completion.commandId",
            "Completion.submissionId",
            "Completion.updateId",
            "Completion.userId",
            "CreatedEvent.contractId",
            "CreatedEvent.contractKeyHash",
            "CreatedEvent.createdEventBlob",
            "CreatedEvent.packageName",
            "CreatedEvent.representativePackageId",
            "ExecuteSubmissionAndWaitResponse.updateId",
            "ExercisedEvent.choice",
            "ExercisedEvent.contractId",
            "ExercisedEvent.packageName",
            "GetActiveContractsPageResponse.nextPageToken",
            "GetUpdatesPageResponse.nextPageToken",
            "Reassignment.commandId",
            "Reassignment.synchronizerId",
            "Reassignment.updateId",
            "Reassignment.workflowId",
            "SubmitAndWaitResponse.updateId",
            "TopologyTransaction.synchronizerId",
            "TopologyTransaction.updateId",
            "Transaction.commandId",
            "Transaction.externalTransactionHash",
            "Transaction.synchronizerId",
            "Transaction.updateId",
            "Transaction.workflowId",
            "UnassignedEvent.contractId",
            "UnassignedEvent.packageName",
            "UnassignedEvent.reassignmentId",
            "UnassignedEvent.source",
            "UnassignedEvent.submitter",
            "UnassignedEvent.target",
        }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> RequestSitesTheParticipantAcceptsInTheDeclaredStringForm =
        new[]
        {
            "GetActiveContractsPageRequest.activeAtOffset",
            "GetActiveContractsRequest.activeAtOffset",
            "GetUpdateByOffsetRequest.offset",
        }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> SitesWhoseServedEnvelopeNestsTheBoundUnderAOneOf =
        new[]
        {
            "MinLedgerTime.minLedgerTimeRel",
        }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> DurationSitesAWholeTypeConverterAlreadyReshapes =
        new[]
        {
            "DeduplicationPeriod.DeduplicationDuration",
        }.ToFrozenSet(StringComparer.Ordinal);

    private const string ClassificationHint =
        "the two site tables are opt-in by type - UseWireEncoding never visits a type they do not "
        + "name - so a property the participant sends as a raw JSON number or as a seconds-and-nanos "
        + "object makes System.Text.Json throw a JsonException where the generated POCO declares a "
        + "string, failing the whole response, while every from-the-table test stays green. "
        + "Regenerating spec/openapi.yaml can add one, so every string property on a type either "
        + "table already claims must be classified here: wire-encoded in WireInt64Sites or "
        + "WireDurationSites, or inventoried as text";

    [Fact]
    public void WireEncodedTypes_declare_no_unclassified_string_property()
    {
        var wireEncoded = WireEncodedSites();

        var unclassified = WireEncodedTypes()
            .SelectMany(StringSitesOf)
            .Where(site => !wireEncoded.Contains(site))
            .Where(site => !SitesCarryingTextRatherThanANumberOrADuration.Contains(site))
            .ToList();

        unclassified.Should().BeEmpty(ClassificationHint);
    }

    [Fact]
    public void WireEncodedTypes_name_exactly_the_types_the_two_tables_are_expected_to_claim()
    {
        var claimed = WireEncodedTypes().Select(type => type.Name).ToList();

        claimed.Should().BeEquivalentTo(
            TypesEitherWireTableClaims,
            "every guard here is driven by the tables' own key set, so dropping a row deletes its own "
            + "coverage: the classification and inventory checks stop seeing the type at all, and the "
            + "site theories build their cases from the same rows. Only a roster fixed outside the "
            + "tables can tell a deliberate removal from a silent one");
    }

    [Fact]
    public void SitesCarryingText_inventory_names_only_properties_the_wire_encoded_types_still_declare()
    {
        var declared = WireEncodedTypes().SelectMany(StringSitesOf).ToHashSet(StringComparer.Ordinal);

        SitesCarryingTextRatherThanANumberOrADuration.Should().OnlyContain(
            site => declared.Contains(site),
            "an inventory entry naming a property no wire-encoded type declares any more means either "
            + "the property was renamed or its owner stopped being wire-encoded, and in the second case "
            + "the rest of that owner's properties silently left the classification above");
    }

    [Fact]
    public void ExemptionSets_name_only_properties_the_generated_wire_surface_still_declares()
    {
        var declared = GeneratedWireSites();

        ExemptedSites().Should().OnlyContain(
            site => declared.Contains(site),
            "an exemption naming a property the generated surface no longer declares is dead weight "
            + "that a later regen can revive against a different property of the same name, exempting "
            + "a site nobody ever decided to exempt");
    }

    [Fact]
    public void GeneratedWireSurface_carries_no_unlisted_property_with_a_wire_encoded_name()
    {
        var wireEncoded = WireEncodedSites();
        var wireEncodedNames = wireEncoded.Select(JsonNameOf).ToHashSet(StringComparer.Ordinal);

        var unlisted = GeneratedWireSites()
            .Where(site => wireEncodedNames.Contains(JsonNameOf(site)))
            .Where(site => !wireEncoded.Contains(site))
            .Where(site => !RequestSitesTheParticipantAcceptsInTheDeclaredStringForm.Contains(site))
            .Where(site => !SitesWhoseServedEnvelopeNestsTheBoundUnderAOneOf.Contains(site))
            .ToList();

        unlisted.Should().BeEmpty(
            "a regen can put a name the tables already treat as a wire-encoded number or duration onto "
            + "a type neither table names, and that new type is never visited - so it must be added to "
            + "the table it belongs in, or to one of the two exemption sets above");
    }

    [Fact]
    public void GeneratedWireSurface_declares_no_proto3_duration_outside_the_duration_table()
    {
        var durationSites = Proto3DurationSites();

        durationSites.Should().NotBeEmpty(
            "a duration site is discovered through the proto3-duration pattern the generator copies out "
            + "of spec/openapi.yaml, so a regen that reshapes or drops that pattern empties this "
            + "universe and leaves every check below trivially satisfied");

        var wireEncoded = WireEncodedSites();

        durationSites
            .Where(site => !wireEncoded.Contains(site))
            .Where(site => !SitesWhoseServedEnvelopeNestsTheBoundUnderAOneOf.Contains(site))
            .Where(site => !DurationSitesAWholeTypeConverterAlreadyReshapes.Contains(site))
            .Should().BeEmpty(
                "unlike an int64, a duration carries a machine-readable discriminator on the generated "
                + "property itself, so WireDurationSites can be held complete against the surface rather "
                + "than against a hand-kept list: every site the generator marks must be reshaped by the "
                + "duration table, or named in one of the exemption sets above");
    }

    [Fact]
    public void DeduplicationPeriod_is_reshaped_by_a_whole_type_converter_rather_than_by_the_duration_table()
    {
        RestRefitSettings.SerializerOptions.Converters.Should().Contain(
            converter => converter.CanConvert(typeof(DeduplicationPeriod)),
            "the duration arm of a deduplication period is exempted from the per-property table because "
            + "the whole type is converted at once - the arm is nested under a value key the property "
            + "converter cannot reach - so the exemption is only honest while that converter is attached");
    }

    private static IEnumerable<string> ExemptedSites() =>
        RequestSitesTheParticipantAcceptsInTheDeclaredStringForm
            .Concat(SitesWhoseServedEnvelopeNestsTheBoundUnderAOneOf)
            .Concat(DurationSitesAWholeTypeConverterAlreadyReshapes);

    private static string JsonNameOf(string site) =>
        site[(site.IndexOf('.', StringComparison.Ordinal) + 1)..];

    private static IEnumerable<Type> WireEncodedTypes() =>
        WireInt64Sites.ByOwner.Keys.Concat(WireDurationSites.ByOwner.Keys).Distinct();

    private static List<Type> RawWireTypes()
    {
        var types = typeof(GetLedgerEndResponse).Assembly
            .GetExportedTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.Namespace == RawNamespace)
            .ToList();

        types.Should().NotBeEmpty(
            "this universe is the generated surface itself, and it empties the moment the Raw types stop "
            + "being exported or move namespace - both live possibilities - after which every check over "
            + "it passes without looking at anything");

        return types;
    }

    private static List<string> GeneratedWireSites() =>
        RawWireTypes().SelectMany(StringSitesOf).ToList();

    private static List<string> Proto3DurationSites() =>
        RawWireTypes()
            .SelectMany(owner => StringPropertiesOf(owner)
                .Where(property =>
                    property.GetCustomAttribute<RegularExpressionAttribute>()?.Pattern == Proto3DurationPattern)
                .Select(property => $"{owner.Name}.{WireNameOf(property)}"))
            .ToList();

    private static IEnumerable<string> StringSitesOf(Type owner) =>
        StringPropertiesOf(owner).Select(property => $"{owner.Name}.{WireNameOf(property)}");

    private static IEnumerable<PropertyInfo> StringPropertiesOf(Type owner) =>
        owner.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(string));

    private static string WireNameOf(PropertyInfo property)
    {
        var wireName = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;

        wireName.Should().NotBeNull(
            $"{property.DeclaringType!.Name}.{property.Name} is a generated string property carrying no "
            + "JsonPropertyName, so no wire-site table can name it and no check here can see it - the "
            + "very hole this file exists to close");

        return wireName!;
    }

    private static HashSet<string> WireEncodedSites() =>
        WireInt64Sites.ByOwner.Concat(WireDurationSites.ByOwner)
            .SelectMany(owner => owner.Value.Select(jsonName => $"{owner.Key.Name}.{jsonName}"))
            .ToHashSet(StringComparer.Ordinal);
}
