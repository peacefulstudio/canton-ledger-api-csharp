// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Canton.Ledger.Kernel.Security;

/// <summary>
/// Builds the shared <see cref="SslClientAuthenticationOptions"/> that the gRPC, HTTP/JSON and
/// PQS clients each hand to a handler they own. The options built here know nothing about any
/// transport — no <c>HttpMessageHandler</c>, <c>SocketsHttpHandler</c>, <c>HttpClient</c> or
/// <c>IHttpClientBuilder</c> is referenced — so the kernel never depends on the primary handler
/// type an <c>IHttpClientFactory</c> happens to construct, which Microsoft documents as an
/// implementation detail a library must not rely on.
/// </summary>
public static class SslClientAuthenticationOptionsFactory
{
    /// <summary>
    /// Creates the client-side TLS options described by <paramref name="options"/>.
    /// </summary>
    /// <param name="options">The TLS material to load and project.</param>
    /// <returns>
    /// <para>
    /// A fresh <see cref="SslClientAuthenticationOptions"/>. A <see cref="TlsOptions"/> whose
    /// <see cref="TlsOptions.IsConfigured"/> is <see langword="false"/> yields an untouched
    /// instance — a genuine no-op that presents no client certificate and validates the peer
    /// against the operating system trust store.
    /// </para>
    /// <para>
    /// A configured client identity is projected onto
    /// <see cref="SslClientAuthenticationOptions.ClientCertificateContext"/>, the member
    /// <c>SslStream</c> consults first and unconditionally. A PEM identity is re-imported with a
    /// persisted private key so Windows accepts it for client authentication.
    /// </para>
    /// <para>
    /// Configured certificate authorities become a
    /// <see cref="SslClientAuthenticationOptions.CertificateChainPolicy"/> with
    /// <see cref="X509ChainTrustMode.CustomRootTrust"/> and the authorities in
    /// <see cref="X509ChainPolicy.CustomTrustStore"/>. When no authorities are configured the
    /// chain policy is left <see langword="null"/> so default chain building, and therefore the
    /// operating system trust store, still applies. A
    /// <see cref="RemoteCertificateValidationCallback"/> is never installed: a callback returning
    /// <see langword="true"/> disables peer validation rather than redirecting it.
    /// </para>
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="System.IO.FileNotFoundException">A configured PEM path — <see cref="TlsOptions.ClientCertificatePemPath"/>, <see cref="TlsOptions.ClientCertificateKeyPemPath"/> or <see cref="TlsOptions.CertificateAuthorityBundlePemPath"/> — does not resolve to a readable file.</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">A configured file does not parse as the certificate material it claims to be, or <see cref="TlsOptions.ClientCertificatePkcs12Path"/> does not resolve to a readable file: <see cref="X509CertificateLoader"/> reports a missing PKCS#12 file as a cryptographic failure wrapping the <see cref="System.IO.FileNotFoundException"/> rather than surfacing it.</exception>
    public static SslClientAuthenticationOptions Create(TlsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var authenticationOptions = new SslClientAuthenticationOptions();

        if (LoadClientCertificate(options) is { } clientCertificate)
        {
            authenticationOptions.ClientCertificateContext =
                SslStreamCertificateContext.Create(clientCertificate, additionalCertificates: null);
        }

        if (LoadCertificateAuthorities(options) is { } certificateAuthorities)
        {
            var chainPolicy = new X509ChainPolicy
            {
                TrustMode = X509ChainTrustMode.CustomRootTrust,
                RevocationMode = options.RevocationMode
            };

            chainPolicy.CustomTrustStore.AddRange(certificateAuthorities);
            authenticationOptions.CertificateChainPolicy = chainPolicy;
        }

        return authenticationOptions;
    }

    private static X509Certificate2? LoadClientCertificate(TlsOptions options)
    {
        if (options.ClientCertificate is not null)
            return options.ClientCertificate;

        if (options.HasClientCertificatePkcs12)
        {
            return X509CertificateLoader.LoadPkcs12FromFile(
                options.ClientCertificatePkcs12Path!,
                options.ClientCertificatePkcs12Password);
        }

        if (options.HasClientCertificatePem)
        {
            return LoadPemWithPersistedKey(
                options.ClientCertificatePemPath!,
                options.ClientCertificateKeyPemPath);
        }

        return null;
    }

    // X509Certificate2.CreateFromPemFile loads the private key into an ephemeral key set, which
    // Windows SChannel cannot use for client authentication — it marshals no in-memory key to
    // LSASS and fails the credential handshake with SEC_E_NO_CREDENTIALS (dotnet/runtime#23749,
    // still open for this API as dotnet/runtime#86328; SslStreamCertificateContext.Create does not
    // sidestep it). The PKCS#12 round-trip below is the workaround the runtime maintainers
    // prescribe, and it runs on every platform so the shipped path is the tested one.
    private static X509Certificate2 LoadPemWithPersistedKey(string certificatePath, string? keyPath)
    {
        using var ephemeral = X509Certificate2.CreateFromPemFile(certificatePath, keyPath);

        var pkcs12 = ephemeral.Export(X509ContentType.Pkcs12);

        try
        {
            return X509CertificateLoader.LoadPkcs12(pkcs12, password: null);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs12);
        }
    }

    private static X509Certificate2Collection? LoadCertificateAuthorities(TlsOptions options)
    {
        if (options.CertificateAuthorities is not null)
            return options.CertificateAuthorities;

        if (!options.HasCertificateAuthorityBundle)
            return null;

        var certificateAuthorities = new X509Certificate2Collection();
        certificateAuthorities.ImportFromPemFile(options.CertificateAuthorityBundlePemPath!);
        return certificateAuthorities;
    }
}
