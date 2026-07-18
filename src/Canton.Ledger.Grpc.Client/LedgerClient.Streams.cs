// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using Canton.Ledger.Kernel.Telemetry;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime;
using Daml.Runtime.Streams;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Grpc.Client;

public sealed partial class LedgerClient
{
    /// <inheritdoc />
    /// <remarks>
    /// Fault contract (ADR 0015): a mid-stream transport fault is surfaced
    /// in-band as a terminal <see cref="CompletionStreamEvent.StreamError"/>,
    /// never thrown — the same value-not-exception contract as
    /// <see cref="SubscribeAsync{T}"/>. A caller cancelling via
    /// <paramref name="cancellationToken"/> still gets an
    /// <see cref="OperationCanceledException"/>.
    /// </remarks>
    public IAsyncEnumerable<CompletionStreamEvent> CompletionStreamAsync(
        RuntimeCommands.SubmitterInfo submitter,
        long beginExclusiveOffset = 0L,
        CancellationToken cancellationToken = default) =>
        CompletionStreamAsyncCore(submitter, beginExclusiveOffset, cancellationToken);

    private async IAsyncEnumerable<CompletionStreamEvent> CompletionStreamAsyncCore(
        RuntimeCommands.SubmitterInfo submitter,
        long beginExclusiveOffset,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var activity = LedgerActivitySource.StartActivity<LedgerClient>(LedgerCallInvoker.Source);
        _invoker.TagServerCall(activity, CommandCompletionService.Descriptor, "CompletionStream");
        activity?.SetTag(LedgerClientActivityTags.CantonFromOffset, beginExclusiveOffset);
        activity.SetSubmitterTags(submitter);

        var request = BuildCompletionStreamRequest(submitter, beginExclusiveOffset);
        LogCompletionStreamStarted(_logger, beginExclusiveOffset);

        using var call = _commandCompletionService.CompletionStream(
            request,
            headers: await _invoker.GetHeadersAsync(cancellationToken).ConfigureAwait(false),
            deadline: null,
            cancellationToken: cancellationToken);

        var stream = call.ResponseStream;

        while (true)
        {
            var step = await StreamMoveResult.NextAsync(stream, cancellationToken).ConfigureAwait(false);
            if (step.Faulted is { } fault)
            {
                LogCompletionStreamError(_logger, fault.StatusCode, fault.Status.Detail);
                activity.RecordGrpcError(fault);
                yield return new CompletionStreamEvent.StreamError(
                    (int)fault.StatusCode,
                    string.IsNullOrEmpty(fault.Status.Detail) ? fault.Message : fault.Status.Detail);
                yield break;
            }

            if (!step.Moved) yield break;

            switch (stream.Current.CompletionResponseCase)
            {
                case CompletionStreamResponse.CompletionResponseOneofCase.Completion:
                    yield return new CompletionStreamEvent.CommandCompleted(stream.Current.Completion);
                    break;
                case CompletionStreamResponse.CompletionResponseOneofCase.OffsetCheckpoint:
                    yield return new CompletionStreamEvent.Checkpoint(stream.Current.OffsetCheckpoint.Offset);
                    break;
                default:
                    LogCompletionStreamVariantSkipped(_logger, stream.Current.CompletionResponseCase);
                    break;
            }
        }
    }

    private CompletionStreamRequest BuildCompletionStreamRequest(
        RuntimeCommands.SubmitterInfo submitter,
        long beginExclusiveOffset)
    {
        var request = new CompletionStreamRequest { BeginExclusive = beginExclusiveOffset };
        if (_options.UserId is not null)
        {
            request.UserId = _options.UserId;
        }

        request.Parties.AddRange(CompletionParties(submitter));
        return request;
    }

    private static IEnumerable<string> CompletionParties(RuntimeCommands.SubmitterInfo submitter)
    {
        var seen = new HashSet<string>();
        foreach (var party in submitter.ActAs)
        {
            if (seen.Add(party.Id)) yield return party.Id;
        }

        foreach (var party in submitter.ReadAs)
        {
            if (seen.Add(party.Id)) yield return party.Id;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Fault contract (ADR 0015): a mid-stream transport fault is surfaced
    /// in-band as a terminal <see cref="ContractStreamEvent{T}.StreamError"/>,
    /// never thrown, so a caller draining with <c>await foreach</c> decides
    /// policy. A caller cancelling via <paramref name="cancellationToken"/>
    /// still gets an <see cref="OperationCanceledException"/>.
    /// </remarks>
    public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
        RuntimeCommands.SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where T : IDamlType
    {
        var filterId = MarkerMatcher<T>.StreamFilterIdentifier();
        return SubscribeAsyncCore<T>(submitter, filterId, fromOffset?.Value, toOffset?.Value, TransactionShape.AcsDelta, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Fault contract (ADR 0015): a mid-stream transport fault is surfaced
    /// in-band as a terminal <see cref="ContractStreamEvent{T}.StreamError"/>,
    /// never thrown. A caller cancelling via <paramref name="cancellationToken"/>
    /// still gets an <see cref="OperationCanceledException"/>.
    /// </remarks>
    public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeLedgerEffectsAsync<T>(
        RuntimeCommands.SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where T : IDamlType
    {
        var filterId = MarkerMatcher<T>.StreamFilterIdentifier();
        return SubscribeAsyncCore<T>(submitter, filterId, fromOffset?.Value, toOffset?.Value, TransactionShape.LedgerEffects, cancellationToken);
    }

    private async IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsyncCore<T>(
        RuntimeCommands.SubmitterInfo submitter,
        ProtoIdentifier filterId,
        long? fromOffset,
        long? toOffset,
        TransactionShape transactionShape,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where T : IDamlType
    {
        using var activity = LedgerActivitySource.StartActivity<LedgerClient>(LedgerCallInvoker.Source);
        _invoker.TagServerCall(activity, UpdateService.Descriptor, "GetUpdates");
        activity?.SetTag(LedgerClientActivityTags.DamlTemplateId, typeof(T).Name);
        activity?.SetTag(LedgerClientActivityTags.CantonFromOffset, fromOffset);
        activity.SetSubmitterTags(submitter);

        var request = SubscribeRequestBuilder.BuildGetUpdatesRequest(
            submitter,
            filterId,
            fromOffset,
            toOffset,
            MarkerMatcher<T>.IsInterface,
            transactionShape);

        LogSubscribeStarted(_logger, typeof(T).Name, fromOffset ?? 0L);

        using var call = _updateService.GetUpdates(
            request,
            headers: await _invoker.GetHeadersAsync(cancellationToken).ConfigureAwait(false),
            deadline: null,
            cancellationToken: cancellationToken);

        var stream = call.ResponseStream;

        while (true)
        {
            var step = await StreamMoveResult.NextAsync(stream, cancellationToken).ConfigureAwait(false);
            if (step.Faulted is { } fault)
            {
                LogSubscribeStreamError(_logger, typeof(T).Name, fault.StatusCode, fault.Status.Detail);
                activity.RecordGrpcError(fault);
                yield return new ContractStreamEvent<T>.StreamError(
                    (int)fault.StatusCode,
                    string.IsNullOrEmpty(fault.Status.Detail) ? fault.Message : fault.Status.Detail);
                yield break;
            }

            if (!step.Moved) yield break;

            foreach (var typedEvent in ProjectUpdate<T>(stream.Current))
            {
                yield return typedEvent;
            }
        }
    }

    private IEnumerable<ContractStreamEvent<T>> ProjectUpdate<T>(
        GetUpdatesResponse response)
        where T : IDamlType
    {
        switch (response.UpdateCase)
        {
            case GetUpdatesResponse.UpdateOneofCase.Transaction:
                foreach (var typedEvent in ContractStreamProjector.ProjectTransactionEvents<T>(response.Transaction, _logger))
                {
                    yield return typedEvent;
                }
                break;
            case GetUpdatesResponse.UpdateOneofCase.OffsetCheckpoint:
                yield return new ContractStreamEvent<T>.Checkpoint(LedgerOffset.At(response.OffsetCheckpoint.Offset));
                break;
            case GetUpdatesResponse.UpdateOneofCase.Reassignment:
                foreach (var typedEvent in ContractStreamProjector.ProjectReassignmentEvents<T>(response.Reassignment, _logger))
                {
                    yield return typedEvent;
                }
                break;
            default:
                LogStreamVariantSkipped(_logger, typeof(T).Name, response.UpdateCase);
                break;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Fault contract (ADR 0015): a mid-snapshot transport fault is surfaced
    /// in-band as a terminal <see cref="AcsSnapshotEntry{T}.StreamError"/>,
    /// never thrown — at parity with <see cref="SubscribeAsync{T}"/>. It is
    /// mutually exclusive with the success-path terminal
    /// <see cref="AcsSnapshotEntry{T}.Checkpoint"/>: a faulted snapshot ends
    /// with <c>StreamError</c> instead, so no snapshot offset is handed over to
    /// a resumed live subscription and the caller must treat the snapshot as
    /// incomplete. When <paramref name="activeAtOffset"/> is null the client
    /// first resolves the ledger end via a unary call before the snapshot stream
    /// opens; a non-cancellation fault during that resolution propagates as a
    /// thrown exception (out of the first enumeration step) rather than a
    /// <c>StreamError</c>, since no snapshot stream has begun. A caller
    /// cancelling via <paramref name="cancellationToken"/> still gets an
    /// <see cref="OperationCanceledException"/>.
    /// </remarks>
    public IAsyncEnumerable<AcsSnapshotEntry<T>> SubscribeActiveAsync<T>(
        RuntimeCommands.SubmitterInfo submitter,
        LedgerOffset? activeAtOffset = null,
        CancellationToken cancellationToken = default)
        where T : IDamlType
    {
        var templateFilter = MarkerMatcher<T>.StreamFilterIdentifier();
        return SubscribeActiveAsyncCore<T>(submitter, templateFilter, activeAtOffset?.Value, cancellationToken);
    }

    private async IAsyncEnumerable<AcsSnapshotEntry<T>> SubscribeActiveAsyncCore<T>(
        RuntimeCommands.SubmitterInfo submitter,
        ProtoIdentifier templateFilter,
        long? activeAtOffset,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where T : IDamlType
    {
        using var activity = LedgerActivitySource.StartActivity<LedgerClient>(LedgerCallInvoker.Source);
        _invoker.TagServerCall(activity, StateService.Descriptor, "GetActiveContracts");
        activity?.SetTag(LedgerClientActivityTags.DamlTemplateId, typeof(T).Name);
        activity.SetSubmitterTags(submitter);

        var effectiveOffset = activeAtOffset ?? (await GetLedgerEndForSnapshotAsync(cancellationToken).ConfigureAwait(false)).Offset;
        var sharedHeaders = await _invoker.GetHeadersAsync(cancellationToken).ConfigureAwait(false);

        var request = SubscribeRequestBuilder.BuildGetActiveContractsRequest(
            submitter,
            templateFilter,
            effectiveOffset,
            MarkerMatcher<T>.IsInterface);

        LogSubscribeActiveStarted(_logger, typeof(T).Name, effectiveOffset);

        using var call = _stateService.GetActiveContracts(
            request,
            headers: sharedHeaders,
            deadline: null,
            cancellationToken: cancellationToken);

        var stream = call.ResponseStream;

        while (true)
        {
            var step = await StreamMoveResult.NextAsync(stream, cancellationToken).ConfigureAwait(false);
            if (step.Faulted is { } fault)
            {
                LogSubscribeStreamError(_logger, typeof(T).Name, fault.StatusCode, fault.Status.Detail);
                activity.RecordGrpcError(fault);
                yield return new AcsSnapshotEntry<T>.StreamError(
                    (int)fault.StatusCode,
                    string.IsNullOrEmpty(fault.Status.Detail) ? fault.Message : fault.Status.Detail);
                yield break;
            }

            if (!step.Moved)
            {
                yield return new AcsSnapshotEntry<T>.Checkpoint(LedgerOffset.At(effectiveOffset));
                yield break;
            }

            foreach (var projected in ContractStreamProjector.ProjectActiveContractEntry<T>(stream.Current, _logger))
            {
                if (projected is ContractStreamEvent<T>.Unclassified unclassified)
                {
                    LogActiveContractEntryUnclassified(_logger, typeof(T).Name, stream.Current.ContractEntryCase, unclassified.Kind);
                }
                yield return ToAcsSnapshotEntry(projected);
            }
        }
    }

    private static AcsSnapshotEntry<T> ToAcsSnapshotEntry<T>(ContractStreamEvent<T> entry)
        where T : IDamlType => entry switch
    {
        ContractStreamEvent<T>.Created created => new AcsSnapshotEntry<T>.Created(
            created.ContractId, created.Payload, created.Offset, created.SynchronizerId, created.WitnessParties),
        ContractStreamEvent<T>.Unassigned unassigned => new AcsSnapshotEntry<T>.Unclassified(
            unassigned.Offset, UnclassifiedKind.UnassignedEvent.ToString()),
        ContractStreamEvent<T>.Unclassified unclassified => new AcsSnapshotEntry<T>.Unclassified(
            unclassified.Offset, unclassified.RawKind ?? unclassified.Kind.ToString()),
        _ => throw new InvalidOperationException(
            $"Active-contract snapshot produced an unexpected entry variant: {entry.GetType().Name}"),
    };

    private async Task<GetLedgerEndResponse> GetLedgerEndForSnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _invoker.InvokeAsync(
                (headers, deadline, token) => _stateService.GetLedgerEndAsync(new GetLedgerEndRequest(), headers, deadline, token),
                cancellationToken).ConfigureAwait(false);
        }
        catch (RpcException ex) when (CallerCancellation.Signals(ex, cancellationToken))
        {
            throw CallerCancellation.AsOperationCanceled(ex, cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Completion stream started from offset {BeginExclusiveOffset}")]
    private static partial void LogCompletionStreamStarted(ILogger logger, long beginExclusiveOffset);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Completion stream failed: {StatusCode} {Detail}")]
    private static partial void LogCompletionStreamError(ILogger logger, StatusCode statusCode, string? detail);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Completion stream skipped variant {Variant}")]
    private static partial void LogCompletionStreamVariantSkipped(ILogger logger, CompletionStreamResponse.CompletionResponseOneofCase variant);

    [LoggerMessage(Level = LogLevel.Information, Message = "Subscribing to {TemplateType} updates from offset {FromOffset}")]
    private static partial void LogSubscribeStarted(ILogger logger, string templateType, long fromOffset);

    [LoggerMessage(Level = LogLevel.Information, Message = "Subscribing to active {TemplateType} contracts at offset {AtOffset}")]
    private static partial void LogSubscribeActiveStarted(ILogger logger, string templateType, long atOffset);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Subscribe stream failed for {TemplateType}: {StatusCode} {Detail}")]
    private static partial void LogSubscribeStreamError(ILogger logger, string templateType, StatusCode statusCode, string detail);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Subscribe stream for {TemplateType} skipped variant {Variant}")]
    private static partial void LogStreamVariantSkipped(ILogger logger, string templateType, GetUpdatesResponse.UpdateOneofCase variant);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Active contracts snapshot for {TemplateType} could not classify entry {ContractEntryCase} — surfaced as Unclassified ({Kind})")]
    private static partial void LogActiveContractEntryUnclassified(ILogger logger, string templateType, GetActiveContractsResponse.ContractEntryOneofCase contractEntryCase, UnclassifiedKind kind);
}
