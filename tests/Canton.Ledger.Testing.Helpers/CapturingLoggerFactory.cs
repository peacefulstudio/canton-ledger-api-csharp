// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Canton.Ledger.Testing.Helpers;

/// <summary>
/// An <see cref="ILoggerFactory"/> that records every log call in memory so tests can assert on
/// what was logged. Every level is enabled and scopes are discarded.
/// </summary>
public sealed class CapturingLoggerFactory : ILoggerFactory
{
    /// <summary>
    /// The log calls recorded so far, in the order the loggers received them.
    /// </summary>
    public ConcurrentQueue<(string Category, LogLevel Level, string Message, Exception? Exception)> Records { get; } = new();

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Records);

    /// <inheritdoc />
    public void AddProvider(ILoggerProvider provider)
    {
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private sealed class CapturingLogger(
        string category,
        ConcurrentQueue<(string Category, LogLevel Level, string Message, Exception? Exception)> records) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            records.Enqueue((category, logLevel, formatter(state, exception), exception));
    }
}
