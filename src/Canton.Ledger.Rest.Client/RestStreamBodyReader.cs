// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Parses the body the Canton JSON Ledger API returns for its bounded, blocking stream endpoints
/// (<c>POST /v2/state/active-contracts</c>, <c>POST /v2/updates</c>): one JSON array of entries,
/// <c>[]</c> when nothing matches. Both endpoints are blocking calls — the whole body is buffered
/// by the transport before this parses it, unlike the gRPC transport's true server streaming.
/// </summary>
internal static class RestStreamBodyReader
{
    /// <summary>Parses <paramref name="body"/> into one <typeparamref name="TEntry"/> per array element.</summary>
    public static IReadOnlyList<TEntry> Parse<TEntry>(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var entries = JsonSerializer.Deserialize<List<TEntry>>(body, RestRefitSettings.SerializerOptions)
            ?? throw new JsonException("The bounded stream response body deserialized to null.");
        if (entries.Any(entry => entry is null))
        {
            throw new JsonException("The bounded stream response body contains a null entry.");
        }
        return entries;
    }
}
