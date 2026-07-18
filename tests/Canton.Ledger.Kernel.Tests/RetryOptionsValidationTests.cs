// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Canton.Ledger.Kernel.Resilience;
using Xunit;

namespace Canton.Ledger.Kernel.Tests;

public class RetryOptionsValidationTests
{
    private static IReadOnlyList<ValidationResult> Validate(RetryOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(
            options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void MaxRetryAttempts_is_rejected_when_negative()
    {
        var results = Validate(new RetryOptions { Enabled = true, MaxRetryAttempts = -1 });

        results.Should().ContainSingle()
            .Which.MemberNames.Should().Contain(nameof(RetryOptions.MaxRetryAttempts));
    }

    [Fact]
    public void Delay_is_rejected_when_negative()
    {
        var results = Validate(new RetryOptions { Enabled = true, Delay = TimeSpan.FromMilliseconds(-1) });

        results.Should().ContainSingle()
            .Which.MemberNames.Should().Contain(nameof(RetryOptions.Delay));
    }

    [Fact]
    public void Default_configuration_passes_validation()
    {
        Validate(new RetryOptions()).Should().BeEmpty();
    }

    [Fact]
    public void Enabled_configuration_with_nonnegative_values_passes_validation()
    {
        var results = Validate(new RetryOptions
        {
            Enabled = true,
            MaxRetryAttempts = 5,
            Delay = TimeSpan.FromMilliseconds(200)
        });

        results.Should().BeEmpty();
    }
}
