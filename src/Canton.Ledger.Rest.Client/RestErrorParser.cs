// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Net;
using System.Text.Json;
using Canton.Ledger.Abstractions;
using Daml.Runtime.Outcomes;
using WireCantonError = Canton.Ledger.Rest.Client.Raw.JsCantonError;
using WireStatus = Canton.Ledger.Rest.Client.Raw.Status;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Parses a JSON Ledger API error response body into the shared <see cref="ParsedLedgerError"/> the
/// gRPC transport's error parser also produces from trailers, so REST and gRPC surface the same
/// structured error to callers — classification included, since both route the wire <c>category</c>
/// through <see cref="ParsedLedgerError.MapCategory"/>.
/// </summary>
/// <remarks>
/// <para>
/// Two JSON body shapes are accepted. The participant's own <c>JsCantonError</c> envelope
/// (<c>code</c>/<c>cause</c>/<c>context</c>/<c>errorCategory</c>) is tried first, because that is
/// what a live participant serves; the <c>google.rpc.Status</c> shape the proto-derived spec
/// declares (<c>code</c>/<c>message</c>/<c>details</c>) remains the fallback. The two are told
/// apart by <c>code</c>, which is a string in the first and an integer in the second.
/// </para>
/// <para>
/// A <c>text/plain</c> body carries neither shape and is read from the response's content type
/// rather than by letting both JSON parsers fail. Every operation the participant serves declares
/// its <c>400</c> as <c>text/plain</c> with a bare <c>string</c> schema — the request-decoding
/// rejection the HTTP layer raises before Canton's error machinery runs — so such a body carries no
/// structured error to recover and lands on <see cref="ParsedLedgerError.Unstructured"/>. Its
/// category is determinate all the same, and a <c>400</c> is
/// classified <see cref="DamlErrorCategory.InvalidIndependentOfSystemState"/>, the category the
/// gRPC transport carries for the same malformed request. This is the HTTP counterpart of the gRPC
/// parser classifying a redacted failure from its status code alone; it is deliberately narrower
/// than "any status with no structured payload", because only the <c>400</c> class has a declared
/// id-less shape.
/// </para>
/// <para>
/// A participant redacts a security-sensitive failure before serving it: the envelope arrives with
/// <c>code</c> <c>NA</c>, a fixed cause, an empty <c>context</c> and an <c>errorCategory</c> of
/// <c>-1</c>. That negative id is the participant declaring it attached no category, which is what
/// routes the envelope to <see cref="ParsedLedgerError.Unstructured"/> — the <c>NA</c> placeholder
/// is a redaction sentinel and is never read as an error id. An envelope carrying a category id the
/// participant did mean, recognised or not, stays <see cref="ParsedLedgerError.Structured"/> and
/// keeps its <c>code</c>.
/// </para>
/// <para>
/// The HTTP status is then the only classification evidence left on a redacted failure, and a
/// <c>401</c> or <c>403</c> that yielded no category is classified from it — the same recovery the
/// gRPC parser performs from <c>Unauthenticated</c> and <c>PermissionDenied</c>. It runs last and
/// only over an unstructured failure carrying no category, so an answer that classified itself
/// keeps its own classification, and it is confined to the two statuses whose only Canton categories
/// are the security ones, so it can never invent a category where the participant supplied better
/// evidence.
/// </para>
/// </remarks>
internal static class RestErrorParser
{
    private const string ErrorInfoTypeSuffix = "/google.rpc.ErrorInfo";
    private const string ReasonPropertyName = "reason";
    private const string MetadataPropertyName = "metadata";
    private const string CategoryMetadataKey = "category";
    private const string PlainTextMediaType = "text/plain";

    private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
        new Dictionary<string, string>(0);

    private static readonly IReadOnlyDictionary<int, DamlErrorCategory> RedactedSecurityCategories =
        new Dictionary<int, DamlErrorCategory>
        {
            [(int)HttpStatusCode.Unauthorized] = DamlErrorCategory.AuthInterceptorInvalidAuthenticationCredentials,
            [(int)HttpStatusCode.Forbidden] = DamlErrorCategory.AuthorizationChecksFailed,
        };

    /// <remarks>
    /// <see cref="ParsedLedgerError.StatusCode"/> is always the response's HTTP status code, never
    /// the wire body's <c>google.rpc.Status.code</c> (a distinct gRPC status code, 0..16) — the two
    /// numbering schemes overlap in range and would be ambiguous to a caller if conflated.
    /// </remarks>
    public static async Task<ParsedLedgerError> ParseAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        var httpStatusCode = (int)response.StatusCode;
        var body = response.Content is null
            ? null
            : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return RecoverAuthCategoryFromHttpStatus(Read(response, body, httpStatusCode), httpStatusCode);
    }

    private static ParsedLedgerError RecoverAuthCategoryFromHttpStatus(
        ParsedLedgerError parsed, int httpStatusCode) =>
        parsed is ParsedLedgerError.Unstructured { Category: null } unclassified
        && RedactedSecurityCategories.TryGetValue(httpStatusCode, out var category)
            ? unclassified with { Category = category }
            : parsed;

    private static ParsedLedgerError Read(HttpResponseMessage response, string? body, int httpStatusCode)
    {
        if (IsPlainText(response.Content))
        {
            return FromPlainText(body, response.ReasonPhrase, httpStatusCode);
        }

        if (TryParseCantonError(body) is { } cantonError)
        {
            return FromCantonError(cantonError, httpStatusCode);
        }

        var status = TryParseStatus(body);
        if (status is null)
        {
            var fallbackMessage = string.IsNullOrEmpty(body) ? response.ReasonPhrase : body;
            return new ParsedLedgerError.Unstructured(fallbackMessage, httpStatusCode);
        }

        var errorInfo = FindErrorInfo(status.Details);
        if (errorInfo is null)
        {
            return new ParsedLedgerError.Unstructured(status.Message, httpStatusCode);
        }

        var metadata = ToMetadata(errorInfo);
        var errorId = errorInfo.AdditionalProperties.TryGetValue(ReasonPropertyName, out var reasonValue)
            ? AsString(reasonValue)
            : string.Empty;

        return new ParsedLedgerError.Structured(
            ParsedLedgerError.MapCategory(metadata.TryGetValue(CategoryMetadataKey, out var raw) ? raw : null),
            errorId,
            status.Message ?? string.Empty,
            metadata,
            httpStatusCode);
    }

    private static bool IsPlainText(HttpContent? content) =>
        string.Equals(content?.Headers.ContentType?.MediaType, PlainTextMediaType, StringComparison.OrdinalIgnoreCase);

    private static ParsedLedgerError FromPlainText(string? body, string? reasonPhrase, int httpStatusCode) =>
        new ParsedLedgerError.Unstructured(
            string.IsNullOrEmpty(body) ? reasonPhrase : body,
            httpStatusCode,
            httpStatusCode == (int)HttpStatusCode.BadRequest
                ? DamlErrorCategory.InvalidIndependentOfSystemState
                : null);

    private static WireCantonError? TryParseCantonError(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            var cantonError = JsonSerializer.Deserialize<WireCantonError>(body, RestRefitSettings.SerializerOptions);
            return string.IsNullOrEmpty(cantonError?.Code) ? null : cantonError;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ParsedLedgerError FromCantonError(WireCantonError cantonError, int httpStatusCode)
    {
        if (DeclaresNoCategory(cantonError))
        {
            return new ParsedLedgerError.Unstructured(cantonError.Cause, httpStatusCode);
        }

        var metadata = ToMetadata(cantonError.Context);
        var wireCategory = cantonError.ErrorCategory?.ToString(CultureInfo.InvariantCulture)
            ?? (metadata.TryGetValue(CategoryMetadataKey, out var raw) ? raw : null);

        return new ParsedLedgerError.Structured(
            ParsedLedgerError.MapCategory(wireCategory),
            cantonError.Code ?? string.Empty,
            cantonError.Cause ?? string.Empty,
            metadata,
            httpStatusCode);
    }

    private static bool DeclaresNoCategory(WireCantonError cantonError) =>
        cantonError.ErrorCategory is < 0;

    private static WireStatus? TryParseStatus(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            return JsonSerializer.Deserialize<WireStatus>(body, RestRefitSettings.SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Raw.GoogleProtobufAny? FindErrorInfo(ICollection<Raw.GoogleProtobufAny>? details)
    {
        if (details is null) return null;
        foreach (var detail in details)
        {
            if (detail?.Type is { } type && type.EndsWith(ErrorInfoTypeSuffix, StringComparison.Ordinal))
            {
                return detail;
            }
        }
        return null;
    }

    private static IReadOnlyDictionary<string, string> ToMetadata(IReadOnlyDictionary<string, string?>? context)
    {
        if (context is null || context.Count == 0)
        {
            return EmptyMetadata;
        }

        var metadata = new Dictionary<string, string>(context.Count, StringComparer.Ordinal);
        foreach (var entry in context)
        {
            metadata[entry.Key] = entry.Value ?? string.Empty;
        }
        return metadata;
    }

    private static IReadOnlyDictionary<string, string> ToMetadata(Raw.GoogleProtobufAny errorInfo)
    {
        if (!errorInfo.AdditionalProperties.TryGetValue(MetadataPropertyName, out var rawMetadata))
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
