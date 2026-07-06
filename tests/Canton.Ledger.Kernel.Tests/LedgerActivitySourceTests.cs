// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using AwesomeAssertions;
using Canton.Ledger.Kernel.Telemetry;
using Xunit;

namespace Canton.Ledger.Kernel.Tests;

public class LedgerActivitySourceTests
{
    [Fact]
    public void NameFor_returns_the_fully_qualified_type_name() =>
        LedgerActivitySource.NameFor<LedgerActivitySourceTests>()
            .Should().Be(typeof(LedgerActivitySourceTests).FullName);

    [Fact]
    public void Create_names_the_ActivitySource_via_NameFor()
    {
        using var source = LedgerActivitySource.Create<LedgerActivitySourceTests>();

        source.Name.Should().Be(LedgerActivitySource.NameFor<LedgerActivitySourceTests>());
    }

    [Fact]
    public void StartActivity_returns_null_when_no_listener_is_registered()
    {
        using var source = new ActivitySource(nameof(StartActivity_returns_null_when_no_listener_is_registered));

        var activity = LedgerActivitySource.StartActivity<LedgerActivitySourceTests>(source);

        activity.Should().BeNull();
    }

    [Fact]
    public void StartActivity_names_the_span_CallerType_dot_CallerMember()
    {
        using var source = new ActivitySource(nameof(StartActivity_names_the_span_CallerType_dot_CallerMember));
        using var listener = new ActivityListener
        {
            ShouldListenTo = candidate => candidate.Name == source.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = LedgerActivitySource.StartActivity<LedgerActivitySourceTests>(source);

        activity.Should().NotBeNull();
        activity!.OperationName.Should().Be(
            $"{nameof(LedgerActivitySourceTests)}.{nameof(StartActivity_names_the_span_CallerType_dot_CallerMember)}");
    }

    [Fact]
    public void StartActivity_defaults_to_ActivityKind_Client()
    {
        using var source = new ActivitySource(nameof(StartActivity_defaults_to_ActivityKind_Client));
        using var listener = new ActivityListener
        {
            ShouldListenTo = candidate => candidate.Name == source.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = LedgerActivitySource.StartActivity<LedgerActivitySourceTests>(source);

        activity.Should().NotBeNull();
        activity!.Kind.Should().Be(ActivityKind.Client);
    }

    [Fact]
    public void StartActivity_honors_an_explicit_ActivityKind()
    {
        using var source = new ActivitySource(nameof(StartActivity_honors_an_explicit_ActivityKind));
        using var listener = new ActivityListener
        {
            ShouldListenTo = candidate => candidate.Name == source.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = LedgerActivitySource.StartActivity<LedgerActivitySourceTests>(source, ActivityKind.Internal);

        activity.Should().NotBeNull();
        activity!.Kind.Should().Be(ActivityKind.Internal);
    }
}
