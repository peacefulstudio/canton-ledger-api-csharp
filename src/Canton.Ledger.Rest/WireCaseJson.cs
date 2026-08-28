// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Shared field lookup for the response converters that bridge the two encodings a Canton
/// participant answers with: the proto snake_case names the vendored OpenAPI POCOs declare via
/// <see cref="JsonPropertyNameAttribute"/> and the camelCase keys the JSON Ledger API (circe)
/// actually sends on the wire.
/// </summary>
internal static class WireCaseJson
{
    internal static bool TryGetEitherCase(
        JsonElement root, string snakeCaseName, string camelCaseName, out JsonElement value)
    {
        if (root.TryGetProperty(snakeCaseName, out value)) return true;
        return root.TryGetProperty(camelCaseName, out value);
    }
}
