// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Commands;
using Canton.Ledger.Kernel.Wire;
using ProtoCompletion = Com.Daml.Ledger.Api.V2.Completion;
using ProtoSynchronizerTime = Com.Daml.Ledger.Api.V2.SynchronizerTime;
using ProtoTraceContext = Com.Daml.Ledger.Api.V2.TraceContext;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Grpc.Client;

internal static class GrpcCompletionProjector
{
    public static CompletionStreamEvent Project(ProtoCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(completion);

        var errorInfo = GrpcErrorDetails.FindErrorInfo(completion.Status);

        return CompletionVerdict.Classify(
            ToCompletion(completion),
            completion.Status?.Code,
            completion.Status?.Message,
            NullIfEmpty(errorInfo?.Reason),
            GrpcErrorDetails.ToMetadata(errorInfo),
            completion.UpdateId);
    }

    private static Completion ToCompletion(ProtoCompletion completion) => new(
        ToRequiredCommandId(completion),
        completion.Offset,
        LedgerWireConversions.ToPartyList(completion.ActAs),
        ToSynchronizerTime(completion.SynchronizerTime),
        NullIfEmpty(completion.SubmissionId),
        NullIfEmpty(completion.UserId),
        completion.DeduplicationPeriodCase == ProtoCompletion.DeduplicationPeriodOneofCase.DeduplicationOffset
            ? completion.DeduplicationOffset
            : null,
        completion.DeduplicationPeriodCase == ProtoCompletion.DeduplicationPeriodOneofCase.DeduplicationDuration
            ? completion.DeduplicationDuration.ToTimeSpan()
            : null,
        completion.PaidTrafficCost,
        ToTraceContext(completion.TraceContext));

    private static TraceContext? ToTraceContext(ProtoTraceContext? traceContext) =>
        traceContext is { Traceparent: { } traceparent } && !string.IsNullOrWhiteSpace(traceparent)
            ? new TraceContext(traceparent, NullIfEmpty(traceContext.Tracestate))
            : null;

    private static RuntimeCommands.CommandId ToRequiredCommandId(ProtoCompletion completion) =>
        string.IsNullOrEmpty(completion.CommandId)
            ? throw MalformedResponse.MissingRequiredField(
                $"the completion at offset {completion.Offset} has no command_id")
            : (RuntimeCommands.CommandId)completion.CommandId;

    private static SynchronizerTime ToSynchronizerTime(ProtoSynchronizerTime? synchronizerTime) =>
        synchronizerTime is null
            ? new SynchronizerTime(string.Empty, default)
            : new SynchronizerTime(
                synchronizerTime.SynchronizerId,
                synchronizerTime.RecordTime?.ToDateTimeOffset() ?? default);

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;
}
