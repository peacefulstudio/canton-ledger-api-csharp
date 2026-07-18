// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WireCreatedEvent = Canton.Ledger.Rest.CreatedEvent;

namespace Canton.Ledger.Rest.Client;

internal static partial class ContractStreamProjector
{
    public static ContractStreamEvent<T> ProjectActiveContractEntry<T>(
        GetActiveContractsResponse response,
        ILogger? logger = null)
        where T : IDamlType
    {
        var (created, synchronizerId, fallbackOffset, rawEntryKind) = response switch
        {
            { ActiveContract: { } activeContract } =>
                (activeContract.CreatedEvent, activeContract.SynchronizerId, 0L, "active-contract"),
            { IncompleteUnassigned: { } incompleteUnassigned } =>
                (incompleteUnassigned.CreatedEvent,
                    incompleteUnassigned.UnassignedEvent?.Source,
                    LenientOffset(incompleteUnassigned.UnassignedEvent?.Offset),
                    "incomplete-unassigned"),
            { IncompleteAssigned: { } incompleteAssigned } =>
                (incompleteAssigned.AssignedEvent?.CreatedEvent,
                    incompleteAssigned.AssignedEvent?.Target,
                    0L,
                    "incomplete-assigned"),
            _ => (null, null, 0L, "empty-contract-entry"),
        };

        if (created is null)
        {
            return new ContractStreamEvent<T>.Unclassified(LedgerOffset.At(fallbackOffset), UnclassifiedKind.Unknown, rawEntryKind);
        }
        var offset = LenientOffset(created.Offset);
        if (!MarkerMatcher<T>.MatchesCreated(created))
        {
            return new ContractStreamEvent<T>.Unclassified(LedgerOffset.At(offset), UnclassifiedKind.CreatedEvent);
        }
        if (string.IsNullOrWhiteSpace(synchronizerId))
        {
            return new ContractStreamEvent<T>.Unclassified(LedgerOffset.At(offset), UnclassifiedKind.MissingSynchronizerId);
        }
        try
        {
            return CreatedFromWire<T>(created, new SynchronizerId(synchronizerId));
        }
        catch (Exception decodeFailure) when (decodeFailure is not OperationCanceledException)
        {
            return DecodeFailureEvent<T>(offset, logger, decodeFailure);
        }
    }

    private static ContractStreamEvent<T> CreatedFromWire<T>(WireCreatedEvent created, SynchronizerId synchronizerId)
        where T : IDamlType
    {
        var contractId = created.ContractId
            ?? throw new InvalidOperationException("Created event has no contract id.");
        return new ContractStreamEvent<T>.Created(
            new ContractId<T>(contractId),
            RestValueDecoder.ToDamlRecord(created.CreateArguments),
            LedgerOffset.At(RestWireConversions.ParseOffset(created.Offset)),
            synchronizerId,
            RestWireConversions.ToPartyList(created.WitnessParties));
    }

    private static long LenientOffset(string? wireOffset) =>
        long.TryParse(wireOffset, NumberStyles.None, CultureInfo.InvariantCulture, out var offset) ? offset : 0L;

    private static ContractStreamEvent<T> DecodeFailureEvent<T>(long offset, ILogger? logger, Exception decodeFailure)
        where T : IDamlType
    {
        LogEventDecodeFailed(logger ?? NullLogger.Instance, typeof(T).Name, offset, decodeFailure);
        return new ContractStreamEvent<T>.Unclassified(LedgerOffset.At(offset), UnclassifiedKind.DecodeFailure);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not decode event at offset {Offset} on the {TemplateType} stream — surfaced as Unclassified (decode-failure)")]
    private static partial void LogEventDecodeFailed(ILogger logger, string templateType, long offset, Exception exception);
}
