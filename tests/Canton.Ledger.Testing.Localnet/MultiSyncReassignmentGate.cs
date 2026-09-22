// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Testing.Localnet;

/// <summary>
/// Lets a dedicated CI lane turn the reassignment harnesses' "multi-synchronizer feature flag is
/// not enabled" skip into a hard failure. Skipping is the right default everywhere else (a
/// developer running against a single-synchronizer LocalNet shouldn't see a failure), but the
/// weekly <c>integration-multisync.yaml</c> lane exists specifically to catch a bootstrap
/// regression that disables cross-synchronizer reassignment — there, a skip must not read as a
/// pass.
/// </summary>
internal static class MultiSyncReassignmentGate
{
    public const string RequireEnvironmentVariable = "CANTON_REASSIGNMENT_REQUIRE_MULTI_SYNC";

    public static bool Required =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RequireEnvironmentVariable));
}
