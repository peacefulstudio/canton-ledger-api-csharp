// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using Canton.Ledger.Rest.Client.Raw;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Binds <see cref="ListKnownPartiesResponse"/> from either the proto snake_case field names the
/// Refitter-generated POCO declares (<c>party_details</c>, <c>next_page_token</c>) or the camelCase
/// keys the JSON Ledger API actually sends on the wire (<c>partyDetails</c>, <c>nextPageToken</c>).
/// </summary>
/// <remarks>
/// The JSON Ledger API's <c>GET /v2/parties</c> answers with camelCase multi-word keys, but
/// the generated <see cref="JsonPropertyNameAttribute"/> metadata only declares the snake_case
/// proto3 JSON names, so without this converter the typed <see cref="ListKnownPartiesResponse.PartyDetails"/>
/// and <see cref="ListKnownPartiesResponse.NextPageToken"/> deserialize to <c>null</c> and the
/// payload is only reachable through <see cref="ListKnownPartiesResponse.AdditionalProperties"/>.
/// Scoped to this one response type deliberately — the sibling multi-word deltas on the surface
/// (<c>GetLedgerApiVersion</c>'s <c>userManagement</c> feature key, <c>GetAuthenticatedUser</c>'s
/// <c>primaryParty</c>) get their own <see cref="FeaturesDescriptorJsonConverter"/> and
/// <see cref="UserJsonConverter"/>.
/// </remarks>
internal sealed class ListKnownPartiesResponseJsonConverter : JsonConverter<ListKnownPartiesResponse>
{
    private const string PartyDetailsSnakeCase = "party_details";
    private const string PartyDetailsCamelCase = "partyDetails";
    private const string NextPageTokenSnakeCase = "next_page_token";
    private const string NextPageTokenCamelCase = "nextPageToken";

    /// <inheritdoc />
    public override ListKnownPartiesResponse Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            var response = new ListKnownPartiesResponse();

            if (WireCaseJson.TryGetEitherCase(root, PartyDetailsSnakeCase, PartyDetailsCamelCase, out var partyDetails))
            {
                response.PartyDetails = partyDetails.ValueKind == JsonValueKind.Null
                    ? null!
                    : partyDetails.Deserialize<ICollection<PartyDetails>>(options)!;
            }

            if (WireCaseJson.TryGetEitherCase(root, NextPageTokenSnakeCase, NextPageTokenCamelCase, out var nextPageToken))
            {
                response.NextPageToken = nextPageToken.ValueKind == JsonValueKind.Null
                    ? null!
                    : nextPageToken.GetString()!;
            }

            foreach (var property in root.EnumerateObject())
            {
                if (IsKnownPropertyName(property.Name)) continue;
                response.AdditionalProperties[property.Name] = property.Value.Clone();
            }

            return response;
        }
        catch (Exception ex) when (ex is not JsonException)
        {
            throw new JsonException(
                "Failed to deserialize ListKnownPartiesResponse from the JSON Ledger API response.", ex);
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ListKnownPartiesResponse value, JsonSerializerOptions options)
        => throw new NotSupportedException(
            $"{nameof(ListKnownPartiesResponseJsonConverter)} is read-only; " +
            $"{nameof(ListKnownPartiesResponse)} is never serialized as a request body.");

    private static bool IsKnownPropertyName(string name) =>
        name is PartyDetailsSnakeCase or PartyDetailsCamelCase or NextPageTokenSnakeCase or NextPageTokenCamelCase;
}
