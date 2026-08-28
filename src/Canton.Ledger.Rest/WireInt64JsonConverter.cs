// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Adapts a transport int64 that our specification declares as the proto3-canonical string to the
/// raw JSON number the Canton JSON Ledger API emits and accepts. Reading tolerates both forms.
/// <para>
/// This converter is attached to individual properties through
/// <see cref="WireInt64Sites.UseWireEncoding"/> and must never be registered globally: it would
/// rewrite every string on the wire, including the Daml <c>Int64</c> values inside a contract
/// payload, which the same server encodes as strings precisely to keep them exact past 2^53.
/// </para>
/// </summary>
/// <remarks>
/// Retired by digital-asset/canton#527. proto3 maps int64 to a JSON string to avoid the precision
/// loss a raw number causes past 2^53, which is why our specification is the correct one here.
/// </remarks>
internal sealed class WireInt64JsonConverter : JsonConverter<string>
{
    internal static readonly WireInt64JsonConverter Instance = new();

    /// <inheritdoc />
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            return reader.TokenType switch
            {
                JsonTokenType.Number => reader.GetInt64().ToString(CultureInfo.InvariantCulture),
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Null => null,
                _ => throw Malformed($"a JSON {reader.TokenType} token"),
            };
        }
        catch (Exception failure) when (failure is not JsonException)
        {
            throw Malformed("a value that could not be read", failure);
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
            throw Malformed($"'{value}'");

        writer.WriteNumberValue(number);
    }

    private static JsonException Malformed(string detail, Exception? cause = null) =>
        new($"Expected an int64 as a JSON number or an integer string, but found {detail}.", cause);
}
