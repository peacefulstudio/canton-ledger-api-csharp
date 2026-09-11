// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;
using WireValue = Canton.Ledger.Rest.Client.Raw.Value;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Adapts the bare <c>{}</c> the Canton JSON Ledger API sends for a unit-valued
/// <see cref="WireValue"/> to <see cref="DamlUnit"/>, on the decode paths that carry no Daml type
/// to resolve it with. Measured against Canton 3.5.11, <c>Archive</c> exercised on a
/// <c>RichTypes:Marker</c> over <c>POST /v2/commands/submit-and-wait-for-transaction</c> answers
/// 200 carrying <c>"exerciseResult":{}</c> — present, an object, and empty.
/// <para>
/// That shape is ambiguous, and this package's own <see cref="DamlLfJsonWriter"/> proves it: it
/// emits the same bare <c>{}</c> for unit, for a record with no fields and for an empty
/// <c>TextMap</c>. Where the caller's choice or template supplies a Daml type,
/// <see cref="RestValueDecoder"/> resolves the three apart through
/// <see cref="Daml.Runtime.Serialization.DamlLfJsonReader"/> and never reaches here. The stream and
/// transaction-tree projectors have no such type, and refusing there would reject every
/// unit-returning choice a participant reports.
/// </para>
/// </summary>
/// <remarks>
/// Not retired by digital-asset/canton#527; Daml-LF JSON is type-directed, so no OpenAPI schema can
/// separate the three shapes that share <c>{}</c>. It retires when the participant distinguishes
/// them on the wire — tagging unit as <c>{"unit":{}}</c>, the form
/// <see cref="RestValueEncoder"/> already writes and <see cref="RestValueDecoder"/> already reads
/// as a recognised arm, would be enough.
/// </remarks>
internal static class WireUnitEncoding
{
    /// <summary>
    /// Returns <see cref="DamlUnit.Instance"/> when <paramref name="value"/> is the bare empty
    /// object, and <see langword="null"/> when it is any other unrecognised shape, which the
    /// caller must refuse rather than guess at.
    /// </summary>
    internal static DamlValue? Decode(WireValue value) =>
        value.AdditionalProperties.Count == 0 ? DamlUnit.Instance : null;
}
