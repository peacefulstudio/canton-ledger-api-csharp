// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AwesomeAssertions;
using Canton.Ledger.Kernel.Security;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public class LedgerGrpcChannelTests : IDisposable
{
    private readonly X509Certificate2 _certificateAuthority = SelfSigned("Canton Test Root");
    private readonly X509Certificate2 _clientCertificate = SelfSigned("Canton Test Client");

    private static LedgerClientOptions NewOptions() =>
        new() { GrpcAddress = "https://localhost:5001" };

    [Fact]
    public void BuildOptions_applies_the_default_keepalive_delay_to_a_SocketsHttpHandler()
    {
        var handler = HandlerFrom(NewOptions());

        handler.KeepAlivePingDelay.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void BuildOptions_carries_the_configured_keepalive_intervals_onto_the_handler()
    {
        var options = NewOptions();
        options.KeepAlivePingDelay = TimeSpan.FromSeconds(15);
        options.KeepAlivePingTimeout = TimeSpan.FromSeconds(5);

        var handler = HandlerFrom(options);

        handler.KeepAlivePingDelay.Should().Be(TimeSpan.FromSeconds(15));
        handler.KeepAlivePingTimeout.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void BuildOptions_owns_and_disposes_the_default_handler_it_created()
    {
        var channelOptions = LedgerGrpcChannel.BuildOptions(NewOptions());

        channelOptions.DisposeHttpClient.Should().BeTrue();
    }

    [Fact]
    public void BuildOptions_preserves_the_MaxMessageSize_on_both_directions()
    {
        var options = NewOptions();
        options.MaxMessageSize = 7 * 1024 * 1024;

        var channelOptions = LedgerGrpcChannel.BuildOptions(options);

        channelOptions.MaxReceiveMessageSize.Should().Be(7 * 1024 * 1024);
        channelOptions.MaxSendMessageSize.Should().Be(7 * 1024 * 1024);
    }

    [Fact]
    public void BuildOptions_honors_a_ConfigureChannel_handler_override()
    {
        var replacement = new SocketsHttpHandler();
        var options = NewOptions();
        options.ConfigureChannel = channel => channel.HttpHandler = replacement;

        var channelOptions = LedgerGrpcChannel.BuildOptions(options);

        channelOptions.HttpHandler.Should().BeSameAs(replacement);
    }

    [Fact]
    public void BuildOptions_runs_ConfigureChannel_after_the_defaults_so_the_hook_wins()
    {
        var options = NewOptions();
        options.ConfigureChannel = channel => channel.MaxReceiveMessageSize = 42;

        var channelOptions = LedgerGrpcChannel.BuildOptions(options);

        channelOptions.MaxReceiveMessageSize.Should().Be(42);
    }

    [Fact]
    public void BuildOptions_carries_the_configured_Tls_material_onto_the_handler_SslOptions()
    {
        var options = NewOptions();
        options.KeepAlivePingDelay = TimeSpan.FromSeconds(15);
        options.KeepAlivePingTimeout = TimeSpan.FromSeconds(5);
        options.Tls = new TlsOptions
        {
            ClientCertificate = _clientCertificate,
            CertificateAuthorities = [_certificateAuthority]
        };

        var handler = HandlerFrom(options);
        var sslOptions = handler.SslOptions;

        handler.KeepAlivePingDelay.Should().Be(TimeSpan.FromSeconds(15));
        handler.KeepAlivePingTimeout.Should().Be(TimeSpan.FromSeconds(5));
        sslOptions.ClientCertificateContext.Should().NotBeNull();
        sslOptions.ClientCertificateContext!.TargetCertificate.Thumbprint
            .Should().Be(_clientCertificate.Thumbprint);
        sslOptions.CertificateChainPolicy.Should().NotBeNull();
        sslOptions.CertificateChainPolicy!.TrustMode.Should().Be(X509ChainTrustMode.CustomRootTrust);
        sslOptions.CertificateChainPolicy.CustomTrustStore.Should().ContainSingle()
            .Which.Thumbprint.Should().Be(_certificateAuthority.Thumbprint);
    }

    [Fact]
    public void BuildOptions_leaves_SslOptions_at_its_defaults_when_Tls_is_unconfigured()
    {
        var sslOptions = HandlerFrom(NewOptions()).SslOptions;

        sslOptions.ClientCertificateContext.Should().BeNull();
        sslOptions.ClientCertificates.Should().BeNull();
        sslOptions.CertificateChainPolicy.Should().BeNull();
        sslOptions.RemoteCertificateValidationCallback.Should().BeNull();
    }

    [Fact]
    public void BuildOptions_lets_a_ConfigureChannel_SslOptions_assignment_override_the_typed_Tls()
    {
        var callerOwned = new SslClientAuthenticationOptions();
        var options = NewOptions();
        options.Tls = new TlsOptions { ClientCertificate = _clientCertificate };
        options.ConfigureChannel = channel =>
            ((SocketsHttpHandler)channel.HttpHandler!).SslOptions = callerOwned;

        HandlerFrom(options).SslOptions.Should().BeSameAs(callerOwned);
    }

    public void Dispose()
    {
        _certificateAuthority.Dispose();
        _clientCertificate.Dispose();
        GC.SuppressFinalize(this);
    }

    private static X509Certificate2 SelfSigned(string commonName)
    {
        using var key = RSA.Create(2048);

        var request = new CertificateRequest(
            $"CN={commonName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1));
    }

    private static SocketsHttpHandler HandlerFrom(LedgerClientOptions options) =>
        LedgerGrpcChannel.BuildOptions(options).HttpHandler
            .Should().BeOfType<SocketsHttpHandler>().Subject;
}
