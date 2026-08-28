// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;

namespace Canton.Ledger.Kernel.Commands;

internal static class CompletionVerdict
{
    public static CompletionStreamEvent Classify(
        Completion completion,
        int? statusCode,
        string? statusMessage,
        string? updateId) =>
        statusCode is null or 0
            ? new CompletionStreamEvent.CommandAccepted(completion, updateId ?? string.Empty)
            : new CompletionStreamEvent.CommandRejected(
                completion,
                new CompletionStatus(statusCode.Value, statusMessage ?? string.Empty));
}
