// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using Canton.Ledger.Rest.Client.Raw;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Adapts the structured <see cref="Identifier"/> our specification declares to the flat
/// <c>"packageId:moduleName:entityName"</c> string the Canton JSON Ledger API accepts and returns.
/// Reading also accepts the structured form, so payloads shaped by our own specification still bind,
/// and reads a JSON null as no identifier, the form every optional identifier takes when unset —
/// <c>interfaceId</c> on an exercise of a template choice, for one.
/// </summary>
/// <remarks>
/// Retired by digital-asset/canton#527, which would have the JSON HTTP API generated from the
/// annotated protobuf definitions this identifier shape comes from.
/// </remarks>
internal sealed class WireIdentifierJsonConverter : JsonConverter<Identifier?>
{
    private const int ComponentCount = 3;
    private const char ComponentSeparator = ':';
    private const string ExpectedForm = "packageId:moduleName:entityName";
    private const string PackageIdProperty = "packageId";
    private const string ModuleNameProperty = "moduleName";
    private const string EntityNameProperty = "entityName";

    /// <inheritdoc />
    public override bool HandleNull => true;

    /// <inheritdoc />
    public override Identifier? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType == JsonTokenType.String)
                return FromFlatForm(reader.GetString());

            if (reader.TokenType == JsonTokenType.StartObject)
                return FromStructuredForm(ref reader);

            throw Malformed($"a JSON {reader.TokenType} value");
        }
        catch (Exception failure) when (failure is not JsonException)
        {
            throw Malformed("a value that could not be read", failure);
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Identifier? value, JsonSerializerOptions options)
    {
        if (value is null)
            throw new JsonException("A template identifier reached the wire as null and cannot be encoded.");

        try
        {
            writer.WriteStringValue(
                string.Join(ComponentSeparator, value.PackageId, value.ModuleName, value.EntityName));
        }
        catch (Exception failure) when (failure is not JsonException)
        {
            throw new JsonException(
                $"A template identifier could not be encoded as '{ExpectedForm}'.", failure);
        }
    }

    private static Identifier FromFlatForm(string? raw)
    {
        if (raw is null)
            throw Malformed("null");

        return FromComponents(raw.Split(ComponentSeparator), raw);
    }

    private static Identifier FromStructuredForm(ref Utf8JsonReader reader)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        string?[] components =
        [
            ComponentOrNull(root, PackageIdProperty),
            ComponentOrNull(root, ModuleNameProperty),
            ComponentOrNull(root, EntityNameProperty),
        ];

        return FromComponents(components, string.Join(ComponentSeparator, components));
    }

    private static string? ComponentOrNull(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var component) && component.ValueKind == JsonValueKind.String
            ? component.GetString()
            : null;

    private static Identifier FromComponents(string?[] components, string raw)
    {
        if (components.Length != ComponentCount || Array.Exists(components, string.IsNullOrEmpty))
            throw Malformed($"'{raw}'");

        try
        {
            return new Identifier
            {
                PackageId = components[0]!,
                ModuleName = components[1]!,
                EntityName = components[2]!,
            };
        }
        catch (ArgumentException rejectedComponent)
        {
            throw Malformed($"'{raw}'", rejectedComponent);
        }
    }

    private static JsonException Malformed(string found, Exception? cause = null) =>
        new($"Expected a template identifier of the form '{ExpectedForm}' but found {found}.", cause);
}
