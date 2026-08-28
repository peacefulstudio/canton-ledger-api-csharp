// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Diagnostic identifiers for the experimental Canton JSON Ledger API (REST) surface.
/// </summary>
public static class CantonRestDiagnostics
{
    /// <summary>
    /// The <see cref="System.Diagnostics.CodeAnalysis.ExperimentalAttribute"/> diagnostic id gating the
    /// experimental REST client surface. Suppress it in source with
    /// <c>#pragma warning disable CANTONREST001</c> — a pragma requires the literal id and cannot take
    /// this constant — or by acknowledging the experimental API project-wide.
    /// </summary>
    public const string ExperimentalDiagnosticId = "CANTONREST001";
}
