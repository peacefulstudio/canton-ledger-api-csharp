// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;

namespace Canton.Ledger.Pqs.Client;

internal static class ActivityHelper
{
    internal const string ErrorType = "error.type";

    public static void RecordException(this Activity? activity, Exception exception)
    {
        if (activity is null) return;

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.SetTag(ErrorType, exception.GetType().FullName);
    }
}
