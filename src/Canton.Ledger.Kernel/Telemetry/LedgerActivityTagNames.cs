// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Kernel.Telemetry;

/// <summary>
/// The SDK-owned names the Canton ledger clients pass to <c>Activity.SetTag</c> — span attributes
/// once exported — so a host can name one in a dashboard query, a sampling rule, an enrichment
/// processor or a redaction filter without hardcoding the string or referencing a concrete client
/// assembly.
/// They fall in three groups: <c>daml.*</c> for Daml-LF source concepts, <c>canton.*</c> for
/// Ledger-API and operational ones, and <c>retry.*</c> for the retry pipeline's own spans.
/// </summary>
/// <remarks>
/// These are the emitted attribute names, not a schema: an operation sets only the attributes it
/// has, so no single span carries all of them. Attribute names are part of the telemetry shape and
/// remain revisable in any preview before 1.0.
/// </remarks>
public static class LedgerActivityTagNames
{
    /// <summary>The Daml choice being exercised on the contract this span submits against.</summary>
    public const string DamlChoice = "daml.choice";

    /// <summary>The contract id the span's exercise command targets.</summary>
    public const string DamlContractId = "daml.contract_id";

    /// <summary>The Daml template or interface the span's operation is scoped to.</summary>
    public const string DamlTemplateId = "daml.template_id";

    /// <summary>The Daml package whose archive the span's operation fetched.</summary>
    public const string DamlPackageId = "daml.package_id";

    /// <summary>The ledger offset the span's operation read at, or the one it observed.</summary>
    public const string CantonOffset = "canton.offset";

    /// <summary>The exclusive offset a streaming span resumed from.</summary>
    public const string CantonFromOffset = "canton.from_offset";

    /// <summary>The comma-separated parties the span's operation acts as.</summary>
    public const string CantonSubmitterActAs = "canton.submitter.act_as";

    /// <summary>The comma-separated parties the span's operation additionally reads as, omitted when it names none.</summary>
    public const string CantonSubmitterReadAs = "canton.submitter.read_as";

    /// <summary>The update id the span's point read asked for.</summary>
    public const string CantonUpdateId = "canton.update_id";

    /// <summary>The party the span's connected-synchronizer lookup was narrowed to.</summary>
    public const string CantonPartyId = "canton.party_id";

    /// <summary>The participant node the span's connected-synchronizer lookup was narrowed to.</summary>
    public const string CantonParticipantId = "canton.participant_id";

    /// <summary>The hint the span's party allocation asked the participant to derive an id from.</summary>
    public const string CantonPartyIdHint = "canton.party_id_hint";

    /// <summary>The user the span's operation creates, or whose rights it lists, grants or revokes.</summary>
    public const string CantonUserId = "canton.user_id";

    /// <summary>The submission id the span's operation carried to the participant.</summary>
    public const string CantonSubmissionId = "canton.submission_id";

    /// <summary>The synchronizer traffic, in bytes, a priced submission is estimated to cost.</summary>
    public const string CantonTrafficCostBytes = "canton.traffic_cost_bytes";

    /// <summary>The number of rows a PQS query span returned to the caller.</summary>
    public const string CantonPqsResultCount = "canton.pqs.result_count";

    /// <summary>The zero-based number of the attempt that just failed and triggered this retry span.</summary>
    public const string RetryAttempt = "retry.attempt";

    /// <summary>The delay, in milliseconds, the pipeline will wait before the next attempt.</summary>
    public const string RetryDelayMs = "retry.delay_ms";

    /// <summary>
    /// Every SDK-owned attribute name, so a host building an allowlist, a redaction filter or a span
    /// processor over them reads the whole set from here rather than enumerating the constants by
    /// hand. Client spans also carry OpenTelemetry semantic-convention attributes — <c>rpc.*</c>,
    /// <c>http.*</c>, <c>url.full</c>, <c>server.address</c>, <c>server.port</c>, <c>error.type</c> —
    /// which are the standard's to name, not this SDK's, and are deliberately absent from this set.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        DamlChoice,
        DamlContractId,
        DamlTemplateId,
        DamlPackageId,
        CantonOffset,
        CantonFromOffset,
        CantonSubmitterActAs,
        CantonSubmitterReadAs,
        CantonUpdateId,
        CantonPartyId,
        CantonParticipantId,
        CantonPartyIdHint,
        CantonUserId,
        CantonSubmissionId,
        CantonTrafficCostBytes,
        CantonPqsResultCount,
        RetryAttempt,
        RetryDelayMs
    ];
}
