// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Canton.Ledger.Kernel.Streams;

internal static partial class StreamEventClassifier
{
    public static bool TryAdmit<TSynchronizerScope>(
        in DecodedStreamEvent<TSynchronizerScope> decoded,
        out TSynchronizerScope synchronizerScope,
        out StreamEntryRefusal refusal)
        where TSynchronizerScope : struct
    {
        if (!decoded.MatchesMarker)
        {
            synchronizerScope = default;
            refusal = new StreamEntryRefusal(LedgerOffset.At(decoded.Offset), decoded.UnmatchedKind);
            return false;
        }
        if (decoded.SynchronizerScope is not { } admitted)
        {
            synchronizerScope = default;
            refusal = new StreamEntryRefusal(LedgerOffset.At(decoded.Offset), UnclassifiedKind.MissingSynchronizerId);
            return false;
        }
        synchronizerScope = admitted;
        refusal = default;
        return true;
    }

    public static bool TryAdmit<T, TSynchronizerScope>(
        in DecodedStreamEvent<TSynchronizerScope> decoded,
        out TSynchronizerScope synchronizerScope,
        [NotNullWhen(false)] out ContractStreamEvent<T>.Unclassified? unclassified)
        where T : ITemplate, IDamlRecord<T>
        where TSynchronizerScope : struct
    {
        if (TryAdmit(decoded, out synchronizerScope, out var refusal))
        {
            unclassified = null;
            return true;
        }

        unclassified = new ContractStreamEvent<T>.Unclassified(refusal.Offset, refusal.Kind);
        return false;
    }

    public static SynchronizerId? Synchronizer(string? wireSynchronizerId) =>
        string.IsNullOrWhiteSpace(wireSynchronizerId) ? null : new SynchronizerId(wireSynchronizerId);

    public static ReassignmentScope? ReassignmentSynchronizers(string? wireSource, string? wireTarget) =>
        string.IsNullOrWhiteSpace(wireSource) || string.IsNullOrWhiteSpace(wireTarget)
            ? null
            : new ReassignmentScope(new SynchronizerId(wireSource), new SynchronizerId(wireTarget));

    public static bool IsNotCancellation(Exception exception) => exception is not OperationCanceledException;

    /// <summary>
    /// Logs an event the transport could not decode and returns the refusal that surfaces it in
    /// band as an <c>Unclassified</c> carrying <see cref="UnclassifiedKind.DecodeFailure"/>.
    /// </summary>
    /// <remarks>
    /// <paramref name="offset"/> is the resume point the refusal reports. On the update streams it
    /// is the offset of the containing update — never an offset read off the individual event,
    /// which the wire leaves unset often enough that reading it would hand a consumer the beginning
    /// of the ledger. A consumer resuming from it therefore re-reads every event in that update,
    /// including the ones it already handled, so it must be idempotent across the whole update
    /// rather than across the single event that failed. On the active-contract snapshot, where
    /// there is no containing update, it is the entry's own offset or the snapshot offset it falls
    /// back to.
    /// </remarks>
    public static StreamEntryRefusal DecodeFailure(string markerName, long offset, ILogger? logger, Exception cause)
    {
        LogEventDecodeFailed(logger ?? NullLogger.Instance, markerName, offset, cause);
        return new StreamEntryRefusal(LedgerOffset.At(offset), UnclassifiedKind.DecodeFailure);
    }

    /// <inheritdoc cref="DecodeFailure(string, long, ILogger?, Exception)"/>
    public static ContractStreamEvent<T>.Unclassified DecodeFailure<T>(long offset, ILogger? logger, Exception cause)
        where T : ITemplate, IDamlRecord<T>
    {
        var refusal = DecodeFailure(typeof(T).Name, offset, logger, cause);
        return new ContractStreamEvent<T>.Unclassified(refusal.Offset, refusal.Kind);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not decode event at offset {Offset} on the {TemplateType} stream — surfaced as Unclassified (decode-failure)")]
    private static partial void LogEventDecodeFailed(ILogger logger, string templateType, long offset, Exception exception);
}
