// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Canton.Ledger.Rest.Client.Raw;
using Refit;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// The <see cref="RefitSettings"/> every Canton JSON Ledger API interface is built with —
/// both the generated per-service interfaces from <c>Canton.Ledger.Rest.Client.Raw</c> and the
/// hand-authored off-spec ones in this package share the same serializer configuration.
/// </summary>
public static class RestRefitSettings
{
    /// <summary>
    /// System.Text.Json options consistent with the generated contracts: property names come
    /// from the generated <see cref="JsonPropertyNameAttribute"/> metadata (camelCase, per
    /// proto3's canonical JSON mapping), and unset optional fields are omitted on the wire as
    /// proto3 JSON does.
    /// </summary>
    internal static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new ListKnownPartiesResponseJsonConverter(),
            new UserJsonConverter(),
            new FeaturesDescriptorJsonConverter(),
            new WireIdentifierJsonConverter(),
            new WireValueJsonConverter(),
            new WireRecordJsonConverter(),
            new DeduplicationPeriodJsonConverter(),
            new WrappedOneOfJsonConverterFactory(
                new WrappedOneOf(typeof(IdentifierFilter)),
                new WrappedOneOf(typeof(CompletionResponse)),
                new WrappedOneOf(typeof(Update)),
                new WrappedOneOf(typeof(TopologyEventEvent)),
                new WrappedOneOf(typeof(ReassignmentCommandCommand)),
                new WrappedOneOf(typeof(ReassignmentEvent), "JsAssignmentEvent"),
                new WrappedOneOf(typeof(RightKind)),
                new WrappedOneOf(typeof(VettedPackagesChangeOperation)),
                new WrappedOneOf(typeof(PriorTopologySerialSerial), "NoPrior")),
        },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { WireInt64Sites.UseWireEncoding, WireDurationSites.UseWireEncoding },
        },
    };

    /// <summary>
    /// Creates the <see cref="RefitSettings"/> a standalone <c>Canton.Ledger.Rest</c> consumer must
    /// build its interfaces with, and the settings the DI registrations use when building interface
    /// instances via <see cref="RestService"/>. Nothing the generated file can carry configures the
    /// serializer options, the null-handling and the URL parameter formatter together, so this method
    /// is the only supported way to get a correct raw client without taking
    /// <c>Canton.Ledger.Rest.Client</c>.
    /// </summary>
    public static RefitSettings Create() => new()
    {
        ContentSerializer = new SystemTextJsonContentSerializer(SerializerOptions),
        UrlParameterFormatter = new SimpleStylePathParameterFormatter(),
    };
}
