// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Canton.Ledger.Kernel.Resilience;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public class LedgerClientOptionsTests
{
    [Fact]
    public void Default_values_are_set_correctly()
    {
        var options = new LedgerClientOptions
        {
            GrpcAddress = "https://localhost:5001"
        };

        options.UserId.Should().BeNull();
        options.MaxMessageSize.Should().Be(100 * 1024 * 1024);
        options.Timeout.Should().Be(TimeSpan.FromSeconds(30));
        options.KeepAlivePingDelay.Should().Be(TimeSpan.FromSeconds(60));
        options.KeepAlivePingTimeout.Should().Be(TimeSpan.FromSeconds(20));
        options.ConfigureChannel.Should().BeNull();
    }

    [Fact]
    public void GrpcAddress_is_rejected_by_data_annotations_when_missing()
    {
        var options = new LedgerClientOptions { GrpcAddress = null! };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(
            options, new ValidationContext(options), results, validateAllProperties: true);

        isValid.Should().BeFalse(
            "AddLedgerOptions wires ValidateDataAnnotations().ValidateOnStart(), so [Required] must reject a missing GrpcAddress at startup");
        results.Should().ContainSingle()
            .Which.MemberNames.Should().Contain(nameof(LedgerClientOptions.GrpcAddress));
    }

    [Fact]
    public void Nested_Retry_MaxRetryAttempts_is_rejected_when_negative()
    {
        var options = new LedgerClientOptions
        {
            GrpcAddress = "https://localhost:5001",
            Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = -1 }
        };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(
            options, new ValidationContext(options), results, validateAllProperties: true);

        isValid.Should().BeFalse(
            "runtime data-annotation validation does not descend into nested options, so LedgerClientOptions recurses into Retry to reject it at startup");
        results.Should().ContainSingle()
            .Which.MemberNames.Should().Contain(nameof(RetryOptions.MaxRetryAttempts));
    }

    [Fact]
    public void Nested_Retry_Delay_is_rejected_when_negative()
    {
        var options = new LedgerClientOptions
        {
            GrpcAddress = "https://localhost:5001",
            Retry = new RetryOptions { Enabled = true, Delay = TimeSpan.FromMilliseconds(-1) }
        };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(
            options, new ValidationContext(options), results, validateAllProperties: true);

        isValid.Should().BeFalse();
        results.Should().ContainSingle()
            .Which.MemberNames.Should().Contain(nameof(RetryOptions.Delay));
    }

    [Fact]
    public void Nested_Retry_with_nonnegative_values_passes_validation()
    {
        var options = new LedgerClientOptions
        {
            GrpcAddress = "https://localhost:5001",
            Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 5, Delay = TimeSpan.FromMilliseconds(200) }
        };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(
            options, new ValidationContext(options), results, validateAllProperties: true);

        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }
}
