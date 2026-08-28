// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Kernel.Commands;

internal static class ReassignmentCommandPolicy
{
    public static string RequireNonEmpty(string value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"A reassignment requires a non-empty {field}.", field)
            : value;
}
