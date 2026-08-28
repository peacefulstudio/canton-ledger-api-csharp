// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Canton.Ledger.Rest.Client.Raw;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Binds <see cref="User.PrimaryParty"/> from either the proto snake_case field name the generated
/// POCO declares (<c>primary_party</c>) or the camelCase key the JSON Ledger API actually sends on
/// the wire (<c>primaryParty</c>).
/// </summary>
/// <remarks>
/// Adaptation delta: the <see cref="JsonPropertyNameAttribute"/> metadata on the generated
/// <see cref="User"/> POCO only declares the snake_case proto3 JSON name <c>primary_party</c>, so
/// without this converter the camelCase <c>primaryParty</c> key the participant answers with lands
/// in <see cref="User.AdditionalProperties"/> and the typed property deserializes to <c>null</c>.
/// Scoped to the multi-word <c>primaryParty</c> delta deliberately — the type's other snake_case
/// fields keep their generated wire names, and the write path preserves the request serialization
/// (<c>CreateUser</c>/<c>UpdateUser</c>) exactly as the generated attributes drive it.
/// </remarks>
internal sealed class UserJsonConverter : JsonConverter<User>
{
    private const string IdName = "id";
    private const string PrimaryPartySnakeCase = "primary_party";
    private const string PrimaryPartyCamelCase = "primaryParty";
    private const string IsDeactivatedName = "is_deactivated";
    private const string MetadataName = "metadata";
    private const string IdentityProviderIdName = "identity_provider_id";

    private static readonly ConditionalWeakTable<JsonSerializerOptions, JsonSerializerOptions> WriteOptionsCache = new();

    /// <inheritdoc />
    public override User Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            var user = new User();

            if (root.TryGetProperty(IdName, out var id))
                user.Id = id.ValueKind == JsonValueKind.Null ? null! : id.GetString()!;

            if (WireCaseJson.TryGetEitherCase(root, PrimaryPartySnakeCase, PrimaryPartyCamelCase, out var primaryParty))
                user.PrimaryParty = primaryParty.ValueKind == JsonValueKind.Null ? null! : primaryParty.GetString()!;

            if (root.TryGetProperty(IsDeactivatedName, out var isDeactivated) && isDeactivated.ValueKind != JsonValueKind.Null)
                user.IsDeactivated = isDeactivated.GetBoolean();

            if (root.TryGetProperty(MetadataName, out var metadata))
                user.Metadata = metadata.ValueKind == JsonValueKind.Null ? null! : metadata.Deserialize<ObjectMeta>(options)!;

            if (root.TryGetProperty(IdentityProviderIdName, out var identityProviderId))
                user.IdentityProviderId = identityProviderId.ValueKind == JsonValueKind.Null ? null! : identityProviderId.GetString()!;

            foreach (var property in root.EnumerateObject())
            {
                if (IsKnownPropertyName(property.Name)) continue;
                user.AdditionalProperties[property.Name] = property.Value.Clone();
            }

            return user;
        }
        catch (Exception ex) when (ex is not JsonException)
        {
            throw new JsonException("Failed to deserialize User from the JSON Ledger API response.", ex);
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, User value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, WithoutThisConverter(options));

    private static JsonSerializerOptions WithoutThisConverter(JsonSerializerOptions options) =>
        WriteOptionsCache.GetValue(options, static source =>
        {
            var copy = new JsonSerializerOptions(source);
            for (var i = copy.Converters.Count - 1; i >= 0; i--)
                if (copy.Converters[i] is UserJsonConverter)
                    copy.Converters.RemoveAt(i);
            return copy;
        });

    private static bool IsKnownPropertyName(string name) =>
        name is IdName or PrimaryPartySnakeCase or PrimaryPartyCamelCase
            or IsDeactivatedName or MetadataName or IdentityProviderIdName;
}
