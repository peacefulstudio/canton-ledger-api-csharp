// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Canton.Ledger.Rest.Client.Raw;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Adapts the <see cref="DeduplicationPeriod"/> our specification declares to the encoding the
/// Canton JSON Ledger API accepts. Both arms wrap their payload in a <c>value</c> object: the
/// offset arm wraps a raw JSON number rather than the proto3-canonical string, and the duration
/// arm wraps a <c>{"seconds":…,"nanos":…}</c> object rather than the proto3-canonical
/// <c>"30s"</c> string. Measured against Canton 3.5.9,
/// <c>{"DeduplicationDuration":"30s"}</c> is refused with
/// <c>Invalid value for: body (Missing required field at 'value')</c>, while
/// <c>{"DeduplicationDuration":{"value":{"seconds":30,"nanos":0}}}</c> is accepted.
/// <para>
/// Reading accepts the shapes our own specification declares alongside the served ones — a bare
/// offset string, a bare duration string, and either arm unwrapped — so payloads written by an
/// older peer still bind. A served <c>seconds</c> is honoured as a JSON number or a string.
/// </para>
/// </summary>
/// <remarks>
/// Retired by digital-asset/canton#527, which would have the JSON HTTP API generated from the
/// annotated protobuf definitions this period shape comes from. proto3 maps int64 to a JSON string
/// precisely to avoid the precision loss a raw number causes past 2^53.
/// </remarks>
internal sealed class DeduplicationPeriodJsonConverter : JsonConverter<DeduplicationPeriod>
{
    private const string OffsetArm = "DeduplicationOffset";
    private const string DurationArm = "DeduplicationDuration";
    private const string WrappedValueProperty = "value";

    /// <inheritdoc />
    public override DeduplicationPeriod Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                throw Malformed($"a JSON {root.ValueKind} value");

            var period = new DeduplicationPeriod();
            foreach (var arm in root.EnumerateObject())
            {
                if (arm.NameEquals(OffsetArm) && TryReadOffset(arm.Value, out var offset))
                    period.DeduplicationOffset = offset;
                else if (arm.NameEquals(DurationArm) && TryReadDuration(arm.Value, out var duration))
                    period.DeduplicationDuration = duration;
                else
                    period.AdditionalProperties[arm.Name] = arm.Value.Clone();
            }

            RejectMoreThanOneArm(period);
            return period;
        }
        catch (Exception failure) when (failure is not JsonException)
        {
            throw Malformed("a value that could not be read", failure);
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DeduplicationPeriod value, JsonSerializerOptions options)
    {
        try
        {
            RejectMoreThanOneArm(value);
            writer.WriteStartObject();

            if (value.DeduplicationOffset is not null)
            {
                writer.WriteStartObject(OffsetArm);
                writer.WriteNumber(WrappedValueProperty, OffsetOf(value.DeduplicationOffset));
                writer.WriteEndObject();
            }

            if (value.DeduplicationDuration is not null)
            {
                var (seconds, nanos) = WireDuration.PartsOf(value.DeduplicationDuration);
                writer.WriteStartObject(DurationArm);
                writer.WriteStartObject(WrappedValueProperty);
                writer.WriteNumber(WireDuration.SecondsProperty, seconds);
                writer.WriteNumber(WireDuration.NanosProperty, nanos);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            foreach (var arm in value.AdditionalProperties)
            {
                writer.WritePropertyName(arm.Key);
                JsonSerializer.Serialize(writer, arm.Value, options);
            }

            writer.WriteEndObject();
        }
        catch (Exception failure) when (failure is not JsonException)
        {
            throw new JsonException("A deduplication period could not be encoded.", failure);
        }
    }

    private static void RejectMoreThanOneArm(DeduplicationPeriod period)
    {
        if (period.DeduplicationOffset is not null && period.DeduplicationDuration is not null)
            throw new JsonException(
                $"A deduplication period selects exactly one arm, but both '{OffsetArm}' and '{DurationArm}' were set.");
    }

    private static bool TryReadOffset(JsonElement arm, [NotNullWhen(true)] out string? offset)
    {
        var scalar = arm.ValueKind == JsonValueKind.Object && arm.TryGetProperty(WrappedValueProperty, out var wrapped)
            ? wrapped
            : arm;

        offset = scalar.ValueKind switch
        {
            JsonValueKind.Number when scalar.TryGetInt64(out var number) => number.ToString(CultureInfo.InvariantCulture),
            JsonValueKind.String => scalar.GetString(),
            _ => null,
        };

        return offset is not null;
    }

    private static bool TryReadDuration(JsonElement arm, [NotNullWhen(true)] out string? duration)
    {
        var payload = arm.ValueKind == JsonValueKind.Object && arm.TryGetProperty(WrappedValueProperty, out var wrapped)
            ? wrapped
            : arm;

        duration = payload.ValueKind switch
        {
            JsonValueKind.String => payload.GetString(),
            JsonValueKind.Object => WireDuration.CanonicalOf(payload),
            _ => null,
        };

        return duration is not null;
    }

    private static long OffsetOf(string offset) =>
        long.TryParse(offset, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new JsonException(
                $"Expected a deduplication offset that is a non-negative integer but found '{offset}'.");

    private static JsonException Malformed(string found, Exception? cause = null) =>
        new($"Expected a deduplication period object but found {found}.", cause);
}
