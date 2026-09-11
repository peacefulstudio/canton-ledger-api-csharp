// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography.X509Certificates;
using AwesomeAssertions;
using Canton.Ledger.Kernel.Resilience;
using Canton.Ledger.Kernel.Security;
using Microsoft.Extensions.Configuration;
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
        options.Tls.IsConfigured.Should().BeFalse();
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
    public void Validate_rejects_Retry_with_negative_MaxRetryAttempts()
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
    public void Validate_rejects_Retry_with_negative_Delay()
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
    public void Validate_passes_when_Retry_has_nonnegative_values()
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

    [Fact]
    public void Validate_rejects_Tls_with_two_client_certificate_sources()
    {
        var options = new LedgerClientOptions
        {
            GrpcAddress = "https://localhost:5001",
            Tls = new TlsOptions
            {
                ClientCertificatePemPath = "client.pem",
                ClientCertificatePkcs12Path = "client.pfx"
            }
        };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(
            options, new ValidationContext(options), results, validateAllProperties: true);

        isValid.Should().BeFalse(
            "runtime data-annotation validation does not descend into nested options, so LedgerClientOptions recurses into Tls to reject it at startup");
        results.Should().ContainSingle()
            .Which.MemberNames.Should().Contain(nameof(TlsOptions.ClientCertificatePemPath));
    }

    [Fact]
    public void Tls_binds_path_forms_from_a_configuration_section()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Canton:Ledger:GrpcAddress"] = "https://localhost:5001",
            ["Canton:Ledger:Tls:ClientCertificatePemPath"] = "client.pem",
            ["Canton:Ledger:Tls:CertificateAuthorityBundlePemPath"] = "ca.pem",
            ["Canton:Ledger:Tls:RevocationMode"] = "Online"
        }).Build();

        var options = new LedgerClientOptions { GrpcAddress = "https://localhost:5001" };
        configuration.GetSection("Canton:Ledger").Bind(options);

        options.Tls.ClientCertificatePemPath.Should().Be("client.pem");
        options.Tls.CertificateAuthorityBundlePemPath.Should().Be("ca.pem");
        options.Tls.RevocationMode.Should().Be(X509RevocationMode.Online);
    }
}
