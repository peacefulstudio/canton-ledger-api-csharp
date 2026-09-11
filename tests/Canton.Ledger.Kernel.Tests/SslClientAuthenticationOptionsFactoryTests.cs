// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AwesomeAssertions;
using Canton.Ledger.Kernel.Security;
using Xunit;

namespace Canton.Ledger.Kernel.Tests;

public sealed class SslClientAuthenticationOptionsFactoryTests : IDisposable
{
    private const string ServerHostName = "localhost";

    private readonly TlsTestMaterial _material = new();
    private readonly X509Certificate2 _certificateAuthority;
    private readonly X509Certificate2 _serverCertificate;
    private readonly X509Certificate2 _clientCertificate;

    public SslClientAuthenticationOptionsFactoryTests()
    {
        _certificateAuthority = _material.CreateCertificateAuthority("Canton Test Root");
        _serverCertificate = _material.IssueServerCertificate(_certificateAuthority, ServerHostName);
        _clientCertificate = _material.IssueClientCertificate(_certificateAuthority, "Canton Test Client");
    }

    [Fact]
    public void Create_throws_when_options_null()
    {
        var act = () => SslClientAuthenticationOptionsFactory.Create(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Create_presents_nothing_when_TlsOptions_is_unconfigured()
    {
        var authenticationOptions = SslClientAuthenticationOptionsFactory.Create(new TlsOptions());

        authenticationOptions.ClientCertificateContext.Should().BeNull();
        authenticationOptions.ClientCertificates.Should().BeNull();
        authenticationOptions.CertificateChainPolicy.Should().BeNull();
    }

    [Fact]
    public void Create_never_installs_a_RemoteCertificateValidationCallback()
    {
        var authenticationOptions = SslClientAuthenticationOptionsFactory.Create(new TlsOptions
        {
            ClientCertificate = _clientCertificate,
            CertificateAuthorities = [_certificateAuthority]
        });

        authenticationOptions.RemoteCertificateValidationCallback.Should().BeNull();
        authenticationOptions.LocalCertificateSelectionCallback.Should().BeNull();
    }

    [Fact]
    public void Create_leaves_CertificateChainPolicy_null_when_no_certificate_authority_is_configured()
    {
        var authenticationOptions = SslClientAuthenticationOptionsFactory.Create(new TlsOptions
        {
            ClientCertificate = _clientCertificate
        });

        authenticationOptions.CertificateChainPolicy.Should().BeNull();
    }

    [Fact]
    public void Create_builds_CustomRootTrust_from_CertificateAuthorities()
    {
        var authenticationOptions = SslClientAuthenticationOptionsFactory.Create(new TlsOptions
        {
            CertificateAuthorities = [_certificateAuthority]
        });

        var chainPolicy = authenticationOptions.CertificateChainPolicy;
        chainPolicy.Should().NotBeNull();
        chainPolicy!.TrustMode.Should().Be(X509ChainTrustMode.CustomRootTrust);
        chainPolicy.CustomTrustStore.Should().ContainSingle()
            .Which.Thumbprint.Should().Be(_certificateAuthority.Thumbprint);
    }

    [Fact]
    public void Create_leaves_CertificateChainPolicy_null_whatever_RevocationMode_asks_for()
    {
        var authenticationOptions = SslClientAuthenticationOptionsFactory.Create(new TlsOptions
        {
            ClientCertificate = _clientCertificate,
            RevocationMode = X509RevocationMode.Online
        });

        authenticationOptions.CertificateChainPolicy.Should().BeNull();
    }

    [Fact]
    public void Create_defaults_the_chain_policy_RevocationMode_to_NoCheck()
    {
        var authenticationOptions = SslClientAuthenticationOptionsFactory.Create(new TlsOptions
        {
            CertificateAuthorities = [_certificateAuthority]
        });

        authenticationOptions.CertificateChainPolicy!.RevocationMode
            .Should().Be(X509RevocationMode.NoCheck);
    }

    [Theory]
    [InlineData(X509RevocationMode.Online)]
    [InlineData(X509RevocationMode.Offline)]
    public void Create_carries_the_configured_RevocationMode_onto_the_chain_policy(
        X509RevocationMode revocationMode)
    {
        var authenticationOptions = SslClientAuthenticationOptionsFactory.Create(new TlsOptions
        {
            CertificateAuthorities = [_certificateAuthority],
            RevocationMode = revocationMode
        });

        authenticationOptions.CertificateChainPolicy!.RevocationMode.Should().Be(revocationMode);
    }

    [Fact]
    public void Create_builds_CustomRootTrust_from_CertificateAuthorityBundlePemPath()
    {
        var secondAuthority = _material.CreateCertificateAuthority("Canton Spare Root");

        var authenticationOptions = SslClientAuthenticationOptionsFactory.Create(new TlsOptions
        {
            CertificateAuthorityBundlePemPath =
                _material.WritePemBundle("ca-bundle.pem", _certificateAuthority, secondAuthority)
        });

        var chainPolicy = authenticationOptions.CertificateChainPolicy;
        chainPolicy.Should().NotBeNull();
        chainPolicy!.TrustMode.Should().Be(X509ChainTrustMode.CustomRootTrust);
        chainPolicy.CustomTrustStore.Count.Should().Be(2);
        chainPolicy.CustomTrustStore.Select(certificate => certificate.Thumbprint)
            .Should().BeEquivalentTo([_certificateAuthority.Thumbprint, secondAuthority.Thumbprint]);
    }

    [Fact]
    public void Create_throws_FileNotFoundException_when_ClientCertificatePemPath_is_absent()
    {
        var act = () => SslClientAuthenticationOptionsFactory.Create(new TlsOptions
        {
            ClientCertificatePemPath = _material.AbsentPath("absent-client.crt")
        });

        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Create_throws_FileNotFoundException_when_ClientCertificateKeyPemPath_is_absent()
    {
        var act = () => SslClientAuthenticationOptionsFactory.Create(new TlsOptions
        {
            ClientCertificatePemPath = _material.WritePemCertificate("keyless-client.crt", _clientCertificate),
            ClientCertificateKeyPemPath = _material.AbsentPath("absent-client.key")
        });

        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Create_throws_CryptographicException_wrapping_the_file_fault_when_ClientCertificatePkcs12Path_is_absent()
    {
        var act = () => SslClientAuthenticationOptionsFactory.Create(new TlsOptions
        {
            ClientCertificatePkcs12Path = _material.AbsentPath("absent-client.pfx")
        });

        act.Should().Throw<CryptographicException>()
            .Which.InnerException.Should().BeOfType<FileNotFoundException>();
    }

    [Fact]
    public void Create_throws_FileNotFoundException_when_CertificateAuthorityBundlePemPath_is_absent()
    {
        var act = () => SslClientAuthenticationOptionsFactory.Create(new TlsOptions
        {
            CertificateAuthorityBundlePemPath = _material.AbsentPath("absent-ca-bundle.pem")
        });

        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public async Task Create_presents_the_client_certificate_loaded_from_ClientCertificatePemPath()
    {
        var presented = await HandshakeAsync(new TlsOptions
        {
            ClientCertificatePemPath = _material.WritePemCertificate("client.crt", _clientCertificate),
            ClientCertificateKeyPemPath = _material.WritePemPrivateKey("client.key", _clientCertificate),
            CertificateAuthorities = [_certificateAuthority]
        });

        presented.Should().Be(_clientCertificate.Thumbprint);
    }

    [Fact]
    public async Task Create_presents_the_client_certificate_loaded_from_a_combined_PEM_file()
    {
        var presented = await HandshakeAsync(new TlsOptions
        {
            ClientCertificatePemPath = _material.WriteCombinedPem("client-combined.pem", _clientCertificate),
            CertificateAuthorities = [_certificateAuthority]
        });

        presented.Should().Be(_clientCertificate.Thumbprint);
    }

    [Fact]
    public async Task Create_presents_the_client_certificate_loaded_from_ClientCertificatePkcs12Path()
    {
        var presented = await HandshakeAsync(new TlsOptions
        {
            ClientCertificatePkcs12Path = _material.WritePkcs12("client.pfx", _clientCertificate, "hunter2"),
            ClientCertificatePkcs12Password = "hunter2",
            CertificateAuthorities = [_certificateAuthority]
        });

        presented.Should().Be(_clientCertificate.Thumbprint);
    }

    [Fact]
    public async Task Create_presents_the_client_certificate_loaded_from_an_unprotected_PKCS12_file()
    {
        var presented = await HandshakeAsync(new TlsOptions
        {
            ClientCertificatePkcs12Path = _material.WritePkcs12("client-open.pfx", _clientCertificate, password: null),
            CertificateAuthorities = [_certificateAuthority]
        });

        presented.Should().Be(_clientCertificate.Thumbprint);
    }

    [Fact]
    public async Task Create_presents_the_client_certificate_supplied_as_an_instance()
    {
        var presented = await HandshakeAsync(new TlsOptions
        {
            ClientCertificate = _clientCertificate,
            CertificateAuthorities = [_certificateAuthority]
        });

        presented.Should().Be(_clientCertificate.Thumbprint);
    }

    [Fact]
    public async Task Create_presents_no_client_certificate_when_only_certificate_authorities_are_configured()
    {
        var presented = await HandshakeAsync(new TlsOptions { CertificateAuthorities = [_certificateAuthority] });

        presented.Should().BeNull();
    }

    [Fact]
    public async Task Create_rejects_the_peer_when_its_root_is_absent_from_the_configured_authorities()
    {
        var unrelatedAuthority = _material.CreateCertificateAuthority("Canton Unrelated Root");

        var act = () => HandshakeAsync(new TlsOptions { CertificateAuthorities = [unrelatedAuthority] });

        await act.Should().ThrowAsync<AuthenticationException>();
    }

    private async Task<string?> HandshakeAsync(TlsOptions options)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var acceptTask = AcceptAsync(listener, cancellationToken);

        try
        {
            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync((IPEndPoint)listener.LocalEndpoint, cancellationToken);

            var authenticationOptions = SslClientAuthenticationOptionsFactory.Create(options);
            authenticationOptions.TargetHost = ServerHostName;
            authenticationOptions.EnabledSslProtocols = SslProtocols.Tls12;

            await using var clientStream = new SslStream(tcpClient.GetStream(), leaveInnerStreamOpen: false);
            await clientStream.AuthenticateAsClientAsync(authenticationOptions, cancellationToken);

            return await acceptTask;
        }
        finally
        {
            listener.Stop();
            await IgnoreFaultAsync(acceptTask);
        }
    }

    private async Task<string?> AcceptAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        using var serverClient = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var serverStream = new SslStream(serverClient.GetStream(), leaveInnerStreamOpen: false);

        await serverStream.AuthenticateAsServerAsync(
            new SslServerAuthenticationOptions
            {
                ServerCertificate = _serverCertificate,
                ClientCertificateRequired = true,
                EnabledSslProtocols = SslProtocols.Tls12,
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            },
            cancellationToken);

        return serverStream.RemoteCertificate?.GetCertHashString();
    }

    private static async Task IgnoreFaultAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
        }
    }

    public void Dispose() => _material.Dispose();
}
