// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;

namespace Canton.Ledger.Rest.Client.Raw;

/// <summary>
/// The error envelope a Canton participant actually serves on the JSON Ledger API's error channel,
/// hand-authored in the off-spec tier because the proto-derived spec cannot express it: the protos
/// carry <c>google.rpc.Status</c> there, and the vendored spec is regenerated from them on every
/// build, so <see cref="Status"/> is what the generated surface declares. The participant's own
/// served OpenAPI document — and its live responses — use this shape instead, with the error id in
/// a string <c>code</c>, the message in <c>cause</c>, the Canton error category as a top-level
/// numeric <c>errorCategory</c>, the metadata flattened into a <c>context</c> map, and no
/// <c>details</c> array at all.
/// </summary>
internal sealed class JsCantonError
{
    /// <summary>The Canton error-code id, e.g. <c>USER_NOT_FOUND</c> — a string, unlike <c>google.rpc.Status.code</c>.</summary>
    [JsonPropertyName("code")]
    public string? Code { get; init; }

    /// <summary>The participant's error message; this envelope carries no <c>message</c> field.</summary>
    [JsonPropertyName("cause")]
    public string? Cause { get; init; }

    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; init; }

    [JsonPropertyName("traceId")]
    public string? TraceId { get; init; }

    /// <summary>The error's metadata, including a <c>category</c> entry holding the same id as <see cref="ErrorCategory"/> in decimal-string form.</summary>
    [JsonPropertyName("context")]
    public IReadOnlyDictionary<string, string?>? Context { get; init; }

    /// <summary>The resources the error names, each a two-element <c>[type, id]</c> pair.</summary>
    [JsonPropertyName("resources")]
    public IReadOnlyList<IReadOnlyList<string>>? Resources { get; init; }

    /// <summary>The Canton error category's numeric id, e.g. <c>11</c> for a missing resource.</summary>
    [JsonPropertyName("errorCategory")]
    public int? ErrorCategory { get; init; }

    [JsonPropertyName("grpcCodeValue")]
    public int? GrpcCodeValue { get; init; }

    [JsonPropertyName("retryInfo")]
    public string? RetryInfo { get; init; }

    [JsonPropertyName("definiteAnswer")]
    public bool? DefiniteAnswer { get; init; }
}
