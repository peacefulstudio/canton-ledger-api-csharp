// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography.X509Certificates;
using AwesomeAssertions;
using Canton.Ledger.Kernel.Security;
using Xunit;

namespace Canton.Ledger.Kernel.Tests;

public sealed class TlsOptionsValidationTests : IDisposable
{
    private readonly TlsTestMaterial _material = new();

    private static IReadOnlyList<ValidationResult> Validate(TlsOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(
            options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void Default_configuration_passes_validation()
    {
        Validate(new TlsOptions()).Should().BeEmpty();
    }

    [Fact]
    public void IsConfigured_is_false_by_default()
    {
        new TlsOptions().IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void RevocationMode_is_NoCheck_by_default()
    {
        new TlsOptions().RevocationMode.Should().Be(X509RevocationMode.NoCheck);
    }

    [Fact]
    public void RevocationMode_alone_makes_IsConfigured_true()
    {
        new TlsOptions { RevocationMode = X509RevocationMode.Online }.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void ClientCertificateKeyPemPath_alone_leaves_IsConfigured_false()
    {
        new TlsOptions { ClientCertificateKeyPemPath = "/etc/canton/client.key" }
            .IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void ClientCertificatePkcs12Password_alone_leaves_IsConfigured_false()
    {
        new TlsOptions { ClientCertificatePkcs12Password = "hunter2" }
            .IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void RevocationMode_is_rejected_without_a_certificate_authority_source()
    {
        var results = Validate(new TlsOptions { RevocationMode = X509RevocationMode.Online });

        results.Should().ContainSingle()
            .Which.MemberNames.Should().Contain(nameof(TlsOptions.RevocationMode));
    }

    [Fact]
    public void RevocationMode_passes_validation_alongside_a_certificate_authority_bundle()
    {
        var results = Validate(new TlsOptions
        {
            RevocationMode = X509RevocationMode.Online,
            CertificateAuthorityBundlePemPath = "/etc/canton/ca.pem"
        });

        results.Should().BeEmpty();
    }

    [Fact]
    public void RevocationMode_passes_validation_alongside_loaded_certificate_authorities()
    {
        var results = Validate(new TlsOptions
        {
            RevocationMode = X509RevocationMode.Offline,
            CertificateAuthorities = [_material.CreateCertificateAuthority("root")]
        });

        results.Should().BeEmpty();
    }

    [Fact]
    public void IsConfigured_is_true_when_any_single_member_is_set()
    {
        TlsOptions[] configured =
        [
            new() { ClientCertificatePemPath = "/etc/canton/client.crt" },
            new() { ClientCertificatePkcs12Path = "/etc/canton/client.pfx" },
            new() { ClientCertificate = _material.CreateSelfSigned("client") },
            new() { CertificateAuthorityBundlePemPath = "/etc/canton/ca.pem" },
            new() { CertificateAuthorities = [_material.CreateCertificateAuthority("root")] },
            new() { RevocationMode = X509RevocationMode.Offline }
        ];

        configured.Should().AllSatisfy(options => options.IsConfigured.Should().BeTrue());
    }

    [Fact]
    public void ClientCertificatePemPath_with_a_separate_key_passes_validation()
    {
        var results = Validate(new TlsOptions
        {
            ClientCertificatePemPath = "/etc/canton/client.crt",
            ClientCertificateKeyPemPath = "/etc/canton/client.key"
        });

        results.Should().BeEmpty();
    }

    [Fact]
    public void ClientCertificatePkcs12Path_with_a_password_passes_validation()
    {
        var results = Validate(new TlsOptions
        {
            ClientCertificatePkcs12Path = "/etc/canton/client.pfx",
            ClientCertificatePkcs12Password = "hunter2"
        });

        results.Should().BeEmpty();
    }

    [Fact]
    public void ClientCertificateKeyPemPath_is_rejected_without_ClientCertificatePemPath()
    {
        var results = Validate(new TlsOptions { ClientCertificateKeyPemPath = "/etc/canton/client.key" });

        results.Should().ContainSingle()
            .Which.MemberNames.Should().Contain(nameof(TlsOptions.ClientCertificateKeyPemPath));
    }

    [Fact]
    public void ClientCertificatePkcs12Password_is_rejected_without_ClientCertificatePkcs12Path()
    {
        var results = Validate(new TlsOptions { ClientCertificatePkcs12Password = "hunter2" });

        results.Should().ContainSingle()
            .Which.MemberNames.Should().Contain(nameof(TlsOptions.ClientCertificatePkcs12Password));
    }

    [Fact]
    public void ClientCertificatePemPath_is_rejected_alongside_ClientCertificatePkcs12Path()
    {
        var results = Validate(new TlsOptions
        {
            ClientCertificatePemPath = "/etc/canton/client.crt",
            ClientCertificatePkcs12Path = "/etc/canton/client.pfx"
        });

        results.Should().ContainSingle()
            .Which.MemberNames.Should().BeEquivalentTo(
                nameof(TlsOptions.ClientCertificatePemPath),
                nameof(TlsOptions.ClientCertificatePkcs12Path));
    }

    [Fact]
    public void ClientCertificate_is_rejected_alongside_ClientCertificatePemPath()
    {
        var results = Validate(new TlsOptions
        {
            ClientCertificatePemPath = "/etc/canton/client.crt",
            ClientCertificate = _material.CreateSelfSigned("client")
        });

        results.Should().ContainSingle()
            .Which.MemberNames.Should().BeEquivalentTo(
                nameof(TlsOptions.ClientCertificatePemPath),
                nameof(TlsOptions.ClientCertificate));
    }

    [Fact]
    public void ClientCertificate_is_rejected_alongside_ClientCertificatePkcs12Path()
    {
        var results = Validate(new TlsOptions
        {
            ClientCertificatePkcs12Path = "/etc/canton/client.pfx",
            ClientCertificate = _material.CreateSelfSigned("client")
        });

        results.Should().ContainSingle()
            .Which.MemberNames.Should().BeEquivalentTo(
                nameof(TlsOptions.ClientCertificatePkcs12Path),
                nameof(TlsOptions.ClientCertificate));
    }

    [Fact]
    public void CertificateAuthorities_is_rejected_alongside_CertificateAuthorityBundlePemPath()
    {
        var results = Validate(new TlsOptions
        {
            CertificateAuthorityBundlePemPath = "/etc/canton/ca.pem",
            CertificateAuthorities = [_material.CreateCertificateAuthority("root")]
        });

        results.Should().ContainSingle()
            .Which.MemberNames.Should().BeEquivalentTo(
                nameof(TlsOptions.CertificateAuthorityBundlePemPath),
                nameof(TlsOptions.CertificateAuthorities));
    }

    [Fact]
    public void CertificateAuthorities_is_rejected_when_empty()
    {
        var results = Validate(new TlsOptions { CertificateAuthorities = [] });

        results.Should().ContainSingle()
            .Which.MemberNames.Should().Contain(nameof(TlsOptions.CertificateAuthorities));
    }

    [Fact]
    public void ClientCertificate_alone_passes_validation()
    {
        var results = Validate(new TlsOptions { ClientCertificate = _material.CreateSelfSigned("client") });

        results.Should().BeEmpty();
    }

    [Fact]
    public void CertificateAuthorities_with_one_root_passes_validation()
    {
        var options = new TlsOptions
        {
            CertificateAuthorities = [_material.CreateCertificateAuthority("root")]
        };

        Validate(options).Should().BeEmpty();
    }

    [Fact]
    public void ClientCertificatePemPath_is_treated_as_unset_when_blank()
    {
        var results = Validate(new TlsOptions
        {
            ClientCertificatePemPath = "   ",
            ClientCertificateKeyPemPath = "/etc/canton/client.key"
        });

        results.Should().ContainSingle()
            .Which.MemberNames.Should().Contain(nameof(TlsOptions.ClientCertificateKeyPemPath));
    }

    public void Dispose() => _material.Dispose();
}
