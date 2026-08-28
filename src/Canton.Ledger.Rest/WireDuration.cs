// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.Json;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Translates between the proto3-canonical duration string our specification declares — <c>"30s"</c>,
/// <c>"1.500s"</c>, <c>"-0.000000001s"</c> — and the <c>{"seconds":…,"nanos":…}</c> object the Canton
/// JSON Ledger API serves and accepts. Every translation is total: a string that is not a
/// proto3-canonical duration, and an object whose parts do not describe one, raise a
/// <see cref="JsonException"/> rather than resolving to some nearby value.
/// </summary>
internal static class WireDuration
{
    internal const string SecondsProperty = "seconds";
    internal const string NanosProperty = "nanos";

    private const int NanosPerSecond = 1_000_000_000;
    private const int NanoDigits = 9;

    /// <summary>
    /// Splits a proto3-canonical duration string into the seconds and nanoseconds the served
    /// <c>Duration</c> object carries. A negative duration yields non-positive parts, as proto3 requires.
    /// </summary>
    internal static (long Seconds, int Nanos) PartsOf(string duration)
    {
        var body = duration.AsSpan();
        if (body.Length < 2 || body[^1] != 's')
            throw Malformed($"'{duration}'");

        body = body[..^1];
        var negative = body[0] == '-';
        if (negative)
            body = body[1..];

        var point = body.IndexOf('.');
        var whole = point < 0 ? body : body[..point];
        var fraction = point < 0 ? ReadOnlySpan<char>.Empty : body[(point + 1)..];

        if (fraction.Length > NanoDigits || (point >= 0 && fraction.IsEmpty)
            || !long.TryParse(whole, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
            throw Malformed($"'{duration}'");

        Span<char> nanoDigits = stackalloc char[NanoDigits];
        fraction.CopyTo(nanoDigits);
        nanoDigits[fraction.Length..].Fill('0');

        if (!int.TryParse(nanoDigits, NumberStyles.None, CultureInfo.InvariantCulture, out var nanos))
            throw Malformed($"'{duration}'");

        return negative ? (-seconds, -nanos) : (seconds, nanos);
    }

    /// <summary>
    /// Renders a served <c>Duration</c> object as the proto3-canonical string. A <c>seconds</c> sent as
    /// a JSON string is honoured alongside the number form, matching what the participant itself accepts,
    /// and an absent part reads as zero.
    /// </summary>
    internal static string CanonicalOf(JsonElement duration) =>
        CanonicalOf(IntegerOf(duration, SecondsProperty), IntegerOf(duration, NanosProperty));

    /// <summary>
    /// Renders seconds and nanoseconds as the proto3-canonical string, with the fraction cut to the
    /// three, six or nine digits proto3's JSON mapping calls for.
    /// </summary>
    internal static string CanonicalOf(long seconds, long nanos)
    {
        if (seconds == long.MinValue || nanos <= -NanosPerSecond || nanos >= NanosPerSecond
            || (seconds > 0 && nanos < 0) || (seconds < 0 && nanos > 0))
            throw Malformed($"seconds {seconds} alongside nanos {nanos}");

        var sign = seconds < 0 || nanos < 0 ? "-" : string.Empty;
        return $"{sign}{Math.Abs(seconds).ToString(CultureInfo.InvariantCulture)}{FractionOf(Math.Abs(nanos))}s";
    }

    /// <summary>
    /// The <see cref="JsonException"/> every duration translation fails with, naming what was found in
    /// place of a proto3-canonical duration.
    /// </summary>
    internal static JsonException Malformed(string found, Exception? cause = null) =>
        new($"Expected a proto3-canonical duration such as '30s' or '1.500s' but found {found}.", cause);

    private static long IntegerOf(JsonElement duration, string property)
    {
        if (!duration.TryGetProperty(property, out var part))
            return 0;

        return part.ValueKind switch
        {
            JsonValueKind.Number when part.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(
                part.GetString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => throw Malformed($"a '{property}' of '{part}'"),
        };
    }

    private static string FractionOf(long nanos) => nanos switch
    {
        0 => string.Empty,
        _ when nanos % 1_000_000 == 0 => $".{(nanos / 1_000_000).ToString("D3", CultureInfo.InvariantCulture)}",
        _ when nanos % 1_000 == 0 => $".{(nanos / 1_000).ToString("D6", CultureInfo.InvariantCulture)}",
        _ => $".{nanos.ToString("D9", CultureInfo.InvariantCulture)}",
    };
}
