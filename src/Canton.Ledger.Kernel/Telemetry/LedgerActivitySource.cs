// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Canton.Ledger.Kernel.Telemetry;

/// <summary>
/// The transport-neutral <see cref="ActivitySource"/> naming and span-start convention
/// shared by every Canton ledger client (ADR 0006). The BCL <see cref="Activity"/> API
/// is used directly — no OpenTelemetry SDK dependency is introduced here; the host wires
/// its own <c>TracerProvider</c> against the sources this type names (ADR 0010).
/// </summary>
public static class LedgerActivitySource
{
    /// <summary>
    /// The <see cref="ActivitySource"/> name convention for a client type: its fully
    /// qualified type name. Register with <c>tracing.AddSource(LedgerActivitySource.NameFor&lt;T&gt;())</c>.
    /// </summary>
    public static string NameFor<T>() => typeof(T).FullName!;

    /// <summary>
    /// Creates the <see cref="ActivitySource"/> for a client type following the shared naming convention.
    /// </summary>
    public static ActivitySource Create<T>() => new(NameFor<T>());

    /// <summary>
    /// Starts an activity named <c>{CallerType}.{CallerMember}</c> on <paramref name="activitySource"/>.
    /// Defaults to <see cref="ActivityKind.Client"/> (ADR 0010): every call site today is an
    /// outbound client-call span (gRPC), the shape OpenTelemetry semantic conventions expect.
    /// Pass <paramref name="kind"/> explicitly for a span that is not itself the RPC client call
    /// (e.g. an internal logical span whose child carries the RPC).
    /// </summary>
    /// <typeparam name="T">The type of the caller (used for the activity name prefix).</typeparam>
    /// <param name="activitySource">The activity source to start the activity on.</param>
    /// <param name="kind">The <see cref="ActivityKind"/> to start the activity with.</param>
    /// <param name="callerMemberName">Automatically populated with the caller's method name.</param>
    /// <returns>The started activity, or <see langword="null"/> if no listeners are registered.</returns>
    public static Activity? StartActivity<T>(
        ActivitySource activitySource,
        ActivityKind kind = ActivityKind.Client,
        [CallerMemberName] string callerMemberName = "") =>
        activitySource.StartActivity($"{typeof(T).Name}.{callerMemberName}", kind);
}
