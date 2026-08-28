// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Commands;
using ProtoCompletion = Com.Daml.Ledger.Api.V2.Completion;
using ProtoSynchronizerTime = Com.Daml.Ledger.Api.V2.SynchronizerTime;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Grpc.Client;

internal static class GrpcCompletionProjector
{
    public static CompletionStreamEvent Project(ProtoCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        return CompletionVerdict.Classify(
            ToCompletion(completion),
            completion.Status?.Code,
            completion.Status?.Message,
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
            : null);

    private static RuntimeCommands.CommandId ToRequiredCommandId(ProtoCompletion completion) =>
        string.IsNullOrEmpty(completion.CommandId)
            ? throw GrpcTransactionResultProjector.MalformedResponse(
                $"the completion at offset {completion.Offset} has no command_id")
            : (RuntimeCommands.CommandId)completion.CommandId;

    private static SynchronizerTime ToSynchronizerTime(ProtoSynchronizerTime? synchronizerTime) =>
        synchronizerTime is null
            ? new SynchronizerTime(string.Empty, default)
            : new SynchronizerTime(
                synchronizerTime.SynchronizerId,
                synchronizerTime.RecordTime?.ToDateTimeOffset() ?? default);

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrEmpty(value) ? null : value;
}
