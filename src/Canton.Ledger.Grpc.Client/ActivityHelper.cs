// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Canton.Ledger.Grpc.Client;

/// <summary>
/// Helper for creating activity spans with consistent naming.
/// </summary>
internal static class ActivityHelper
{
    /// <summary>
    /// Starts an activity with the name derived from the calling type and method.
    /// </summary>
    /// <typeparam name="T">The type of the caller (used for the activity name prefix).</typeparam>
    /// <param name="activitySource">The activity source to use.</param>
    /// <param name="callerMemberName">Automatically populated with the caller's method name.</param>
    /// <returns>The started activity, or null if no listeners are registered.</returns>
    public static Activity? StartActivity<T>(
        ActivitySource activitySource,
        [CallerMemberName] string callerMemberName = "")
    {
        return activitySource.StartActivity($"{typeof(T).Name}.{callerMemberName}");
    }
}
