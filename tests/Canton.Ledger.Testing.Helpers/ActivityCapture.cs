// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Diagnostics;

namespace Canton.Ledger.Testing.Helpers;

/// <summary>
/// Captures the <see cref="Activity"/> instances that the calling test's own operation emits on a
/// named <see cref="ActivitySource"/>.
/// </summary>
/// <remarks>
/// An <see cref="ActivityListener"/> matched on a source <em>name</em> observes every activity that
/// source emits process-wide, so a test that captures by name alone also captures activities emitted
/// by tests running concurrently in other xUnit collections — an assertion such as
/// <c>ContainSingle</c> then sees a foreign activity and fails. This capture opens its own root
/// activity and admits only activities carrying the same <see cref="ActivityTraceId"/>. Because
/// <see cref="Activity.Current"/> flows with the async context, an activity started by a concurrent
/// test belongs to a different trace and cannot be observed here, whatever the test schedule.
/// </remarks>
public sealed class ActivityCapture : IDisposable
{
    private readonly ActivitySource _scopeSource = new("Canton.Ledger.Testing.Helpers.ActivityCapture.Scope");
    private readonly ConcurrentQueue<Activity> _captured = new();
    private readonly string _sourceName;
    private readonly ActivityListener _listener;
    private readonly Activity? _scope;

    private ActivityCapture(string sourceName)
    {
        _sourceName = sourceName;
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == sourceName || ReferenceEquals(source, _scopeSource),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = Capture
        };
        ActivitySource.AddActivityListener(_listener);
        _scope = _scopeSource.StartActivity(nameof(ActivityCapture), ActivityKind.Internal);
    }

    /// <summary>
    /// Starts capturing activities emitted on <paramref name="sourceName"/> by the calling test's
    /// async context. Dispose to stop capturing.
    /// </summary>
    /// <param name="sourceName">The <see cref="ActivitySource.Name"/> to capture.</param>
    public static ActivityCapture Of(string sourceName) => new(sourceName);

    /// <summary>
    /// The activities the calling test's own operation emitted, in the order they were started.
    /// </summary>
    public IReadOnlyList<Activity> Activities => [.. _captured];

    /// <summary>Stops capturing and closes the scope this capture opened.</summary>
    public void Dispose()
    {
        _scope?.Dispose();
        _listener.Dispose();
        _scopeSource.Dispose();
    }

    private void Capture(Activity activity)
    {
        if (activity.Source.Name != _sourceName) return;
        if (_scope is null || activity.TraceId != _scope.TraceId) return;
        _captured.Enqueue(activity);
    }
}
