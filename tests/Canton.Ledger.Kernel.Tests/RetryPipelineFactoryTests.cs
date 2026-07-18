// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Kernel.Resilience;
using Polly;
using Xunit;

namespace Canton.Ledger.Kernel.Tests;

public class RetryPipelineFactoryTests
{
    [Fact]
    public void Create_returns_the_empty_pipeline_when_RetryOptions_disabled_by_default()
    {
        var pipeline = RetryPipelineFactory.Create(new RetryOptions());

        pipeline.Should().BeSameAs(ResiliencePipeline.Empty);
    }

    [Fact]
    public void Create_throws_when_options_null()
    {
        var act = () => RetryPipelineFactory.Create(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Create_retries_the_configured_number_of_times_when_enabled()
    {
        var pipeline = RetryPipelineFactory.Create(new RetryOptions
        {
            Enabled = true,
            MaxRetryAttempts = 2,
            Delay = TimeSpan.Zero
        });

        var attempts = 0;

        await pipeline.ExecuteAsync(_ =>
        {
            attempts++;
            if (attempts <= 2)
                throw new InvalidOperationException("transient");

            return ValueTask.CompletedTask;
        }, TestContext.Current.CancellationToken);

        attempts.Should().Be(3);
    }

    [Fact]
    public async Task Create_applies_jitter_so_repeated_first_retry_delays_are_not_identical()
    {
        var observedDelays = new List<TimeSpan>();

        var pipeline = RetryPipelineFactory.Create(
            new RetryOptions
            {
                Enabled = true,
                MaxRetryAttempts = 1,
                Delay = TimeSpan.FromMilliseconds(5)
            },
            onRetry: attempt => observedDelays.Add(attempt.RetryDelay));

        for (var i = 0; i < 25; i++)
        {
            try
            {
                await pipeline.ExecuteAsync(
                    _ => throw new InvalidOperationException("transient"),
                    TestContext.Current.CancellationToken);
            }
            catch (InvalidOperationException)
            {
            }
        }

        observedDelays.Should().HaveCount(25);
        observedDelays.Distinct().Should().HaveCountGreaterThan(1);
    }
}
