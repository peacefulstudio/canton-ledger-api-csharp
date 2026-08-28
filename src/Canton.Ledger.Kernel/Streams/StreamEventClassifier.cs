// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using Daml.Runtime;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Canton.Ledger.Kernel.Streams;

internal static partial class StreamEventClassifier
{
    public static bool TryAdmit<T, TSynchronizerScope>(
        in DecodedStreamEvent<TSynchronizerScope> decoded,
        out TSynchronizerScope synchronizerScope,
        [NotNullWhen(false)] out ContractStreamEvent<T>.Unclassified? unclassified)
        where T : IDamlType
        where TSynchronizerScope : struct
    {
        if (!decoded.MatchesMarker)
        {
            synchronizerScope = default;
            unclassified = new ContractStreamEvent<T>.Unclassified(
                LedgerOffset.At(decoded.Offset), decoded.UnmatchedKind);
            return false;
        }
        if (decoded.SynchronizerScope is not { } admitted)
        {
            synchronizerScope = default;
            unclassified = new ContractStreamEvent<T>.Unclassified(
                LedgerOffset.At(decoded.Offset), UnclassifiedKind.MissingSynchronizerId);
            return false;
        }
        synchronizerScope = admitted;
        unclassified = null;
        return true;
    }

    public static SynchronizerId? Synchronizer(string? wireSynchronizerId) =>
        string.IsNullOrWhiteSpace(wireSynchronizerId) ? null : new SynchronizerId(wireSynchronizerId);

    public static ReassignmentScope? ReassignmentSynchronizers(string? wireSource, string? wireTarget) =>
        string.IsNullOrWhiteSpace(wireSource) || string.IsNullOrWhiteSpace(wireTarget)
            ? null
            : new ReassignmentScope(new SynchronizerId(wireSource), new SynchronizerId(wireTarget));

    public static bool IsDecodeFailure(Exception exception) => exception is not OperationCanceledException;

    public static ContractStreamEvent<T>.Unclassified DecodeFailure<T>(long offset, ILogger? logger, Exception cause)
        where T : IDamlType
    {
        LogEventDecodeFailed(logger ?? NullLogger.Instance, typeof(T).Name, offset, cause);
        return new ContractStreamEvent<T>.Unclassified(LedgerOffset.At(offset), UnclassifiedKind.DecodeFailure);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not decode event at offset {Offset} on the {TemplateType} stream — surfaced as Unclassified (decode-failure)")]
    private static partial void LogEventDecodeFailed(ILogger logger, string templateType, long offset, Exception exception);
}
