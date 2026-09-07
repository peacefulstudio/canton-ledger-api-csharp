// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Canton.Ledger.Kernel.Streams;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Parses the body the Canton JSON Ledger API returns for one window of a streaming read
/// (<c>POST /v2/state/active-contracts</c>, <c>POST /v2/updates</c>,
/// <c>POST /v2/commands/completions</c>): one JSON array of entries, <c>[]</c> when the window
/// closed with nothing to report. Each window is a blocking call whose whole body is buffered by
/// the transport before this parses it, unlike the gRPC transport's true server streaming; the
/// pagination loop stitches the windows back into one continuous stream.
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

    /// <summary>
    /// Parses <paramref name="body"/> as <see cref="Parse{TEntry}"/> does, handing back the decode
    /// failure instead of throwing it. The pagination loop reports a window it cannot read as a
    /// terminal in-band stream error, so the failure has to be a value it can carry.
    /// </summary>
    public static bool TryParse<TEntry>(
        string body,
        out IReadOnlyList<TEntry> entries,
        [NotNullWhen(false)] out Exception? decodeFailure)
    {
        try
        {
            entries = Parse<TEntry>(body);
            decodeFailure = null;
            return true;
        }
        catch (Exception failure) when (StreamEventClassifier.IsNotCancellation(failure))
        {
            entries = [];
            decodeFailure = failure;
            return false;
        }
    }
}
