// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;
using System.Text.Json;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Reads the <c>google.rpc.ErrorInfo</c> entry of a JSON <c>status.details</c> array, shared by the
/// error parser (which reads it off a failed response body) and the completion projector (which
/// reads it off a rejected completion's status). The HTTP counterpart of
/// <c>Canton.Ledger.Grpc.Client.GrpcErrorDetails</c>: every other <c>@type</c> is skipped, and a
/// detail whose shape does not match reads as no reason and no metadata, so neither caller can be
/// made to throw by what a participant put on the wire.
/// </summary>
internal static class RestErrorDetails
{
    private const string ErrorInfoTypeSuffix = "/google.rpc.ErrorInfo";
    private const string ReasonPropertyName = "reason";
    private const string MetadataPropertyName = "metadata";

    private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
        FrozenDictionary<string, string>.Empty;

    public static Raw.GoogleProtobufAny? FindErrorInfo(ICollection<Raw.GoogleProtobufAny>? details)
    {
        if (details is null)
            return null;

        foreach (var detail in details)
        {
            if (detail?.Type is { } type && type.EndsWith(ErrorInfoTypeSuffix, StringComparison.Ordinal))
            {
                return detail;
            }
        }

        return null;
    }

    public static string ReadErrorId(Raw.GoogleProtobufAny? errorInfo) =>
        errorInfo?.AdditionalProperties.TryGetValue(ReasonPropertyName, out var reason) is true
            ? AsString(reason)
            : string.Empty;

    public static IReadOnlyDictionary<string, string> ToMetadata(Raw.GoogleProtobufAny? errorInfo)
    {
        if (errorInfo?.AdditionalProperties.TryGetValue(MetadataPropertyName, out var rawMetadata) is not true)
        {
            return EmptyMetadata;
        }

        if (rawMetadata is not JsonElement { ValueKind: JsonValueKind.Object } metadataElement)
        {
            return EmptyMetadata;
        }

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in metadataElement.EnumerateObject())
        {
            metadata[property.Name] = AsString(property.Value);
        }

        return metadata;
    }

    private static string AsString(object value) =>
        value switch
        {
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString() ?? string.Empty,
            JsonElement element => element.GetRawText(),
            _ => value.ToString() ?? string.Empty,
        };
}
