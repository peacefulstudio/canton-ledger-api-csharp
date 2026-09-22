// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Canton.Ledger.Rest.Client;

internal static class ReflectiveCall
{
    public static object? Unwrapped(Func<object?> reflectiveCall)
    {
        try
        {
            return reflectiveCall();
        }
        catch (TargetInvocationException wrapper) when (wrapper.InnerException is { } cause)
        {
            ExceptionDispatchInfo.Capture(cause).Throw();
            throw;
        }
    }
}
