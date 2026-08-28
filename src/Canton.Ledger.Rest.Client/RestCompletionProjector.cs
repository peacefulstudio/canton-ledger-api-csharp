// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Commands;
using RuntimeCommands = Daml.Runtime.Commands;
using WireCompletion = Canton.Ledger.Rest.Client.Raw.Completion;
using WireSynchronizerTime = Canton.Ledger.Rest.Client.Raw.SynchronizerTime;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Decodes a wire <see cref="WireCompletion"/> into the neutral <see cref="Completion"/> shape and
/// hands it to the shared <c>CompletionVerdict</c> for the accepted/rejected split. Mirrors the gRPC
/// transport's <c>GrpcCompletionProjector</c>, which decodes the same completion from protobuf.
/// </summary>
internal static class RestCompletionProjector
{
    public static CompletionStreamEvent Project(WireCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(completion);

        return CompletionVerdict.Classify(
            ToCompletion(completion),
            completion.Status?.Code,
            completion.Status?.Message,
            completion.UpdateId);
    }

    private static Completion ToCompletion(WireCompletion completion) => new(
        ToRequiredCommandId(completion),
        RestWireConversions.ParseOffset(completion.Offset),
        RestWireConversions.ToPartyList(completion.ActAs),
        ToSynchronizerTime(completion.SynchronizerTime),
        NullIfEmpty(completion.SubmissionId),
        NullIfEmpty(completion.UserId),
        NullIfEmpty(completion.DeduplicationPeriod?.DeduplicationOffset) is { } deduplicationOffset
            ? RestWireConversions.ParseOffset(deduplicationOffset)
            : null,
        NullIfEmpty(completion.DeduplicationPeriod?.DeduplicationDuration) is { } deduplicationDuration
            ? ParseProtobufDuration(deduplicationDuration)
            : null);

    private static SynchronizerTime ToSynchronizerTime(WireSynchronizerTime? synchronizerTime) =>
        synchronizerTime is null
            ? new SynchronizerTime(string.Empty, default)
            : new SynchronizerTime(
                synchronizerTime.SynchronizerId ?? string.Empty,
                synchronizerTime.RecordTime ?? default);

    private static RuntimeCommands.CommandId ToRequiredCommandId(WireCompletion completion) =>
        string.IsNullOrEmpty(completion.CommandId)
            ? throw RestTransactionResultProjector.MalformedResponse(
                $"the completion at offset '{completion.Offset}' has no commandId")
            : (RuntimeCommands.CommandId)completion.CommandId;

    private static TimeSpan ParseProtobufDuration(string duration)
    {
        var seconds = duration.EndsWith('s')
            ? duration[..^1]
            : throw new FormatException($"Cannot parse wire deduplication duration '{duration}' as a protobuf Duration string.");

        return double.TryParse(seconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedSeconds)
            ? TimeSpan.FromSeconds(parsedSeconds)
            : throw new FormatException($"Cannot parse wire deduplication duration '{duration}' as a protobuf Duration string.");
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;
}
