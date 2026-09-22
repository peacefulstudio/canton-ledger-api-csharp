// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Canton.Ledger.Kernel.Security;

internal static class SslClientAuthenticationOptionsFactory
{
    internal static SslClientAuthenticationOptions Create(TlsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var authenticationOptions = new SslClientAuthenticationOptions();

        if (LoadClientCertificate(options) is { } clientIdentity)
        {
            authenticationOptions.ClientCertificateContext =
                SslStreamCertificateContext.Create(
                    clientIdentity.Certificate,
                    clientIdentity.AdditionalCertificates);
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

    private static (
        X509Certificate2 Certificate,
        X509Certificate2Collection? AdditionalCertificates)? LoadClientCertificate(TlsOptions options)
    {
        if (options.ClientCertificate is not null)
            return (options.ClientCertificate, null);

        if (options.HasClientCertificatePkcs12)
        {
            return LoadPkcs12Identity(
                options.ClientCertificatePkcs12Path!,
                options.ClientCertificatePkcs12Password);
        }

        if (options.HasClientCertificatePem)
        {
            return LoadPemWithPersistedKey(
                options.ClientCertificatePemPath!,
                string.IsNullOrWhiteSpace(options.ClientCertificateKeyPemPath)
                    ? null
                    : options.ClientCertificateKeyPemPath);
        }

        return null;
    }

    private static (
        X509Certificate2 Certificate,
        X509Certificate2Collection AdditionalCertificates) LoadPkcs12Identity(
            string path,
            string? password)
    {
        var certificates = X509CertificateLoader.LoadPkcs12CollectionFromFile(path, password);
        var certificate = certificates.Cast<X509Certificate2>().FirstOrDefault(value => value.HasPrivateKey)
            ?? throw new CryptographicException("The PKCS#12 client identity contains no certificate with a private key.");

        certificates.Remove(certificate);
        return (certificate, certificates);
    }

    // X509Certificate2.CreateFromPemFile loads the private key into an ephemeral key set, which
    // Windows SChannel cannot use for client authentication — it marshals no in-memory key to
    // LSASS and fails the credential handshake with SEC_E_NO_CREDENTIALS (dotnet/runtime#23749,
    // still open for this API as dotnet/runtime#86328; SslStreamCertificateContext.Create does not
    // sidestep it). The PKCS#12 round-trip below is the workaround the runtime maintainers
    // prescribe, and it runs on every platform so the shipped path is the tested one.
    private static (
        X509Certificate2 Certificate,
        X509Certificate2Collection AdditionalCertificates) LoadPemWithPersistedKey(
            string certificatePath,
            string? keyPath)
    {
        using var ephemeral = X509Certificate2.CreateFromPemFile(certificatePath, keyPath);

        var pkcs12 = ephemeral.Export(X509ContentType.Pkcs12);

        try
        {
            var certificate = X509CertificateLoader.LoadPkcs12(pkcs12, password: null);
            try
            {
                return (certificate, LoadChainWithout(certificate, certificatePath));
            }
            catch
            {
                certificate.Dispose();
                throw;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs12);
        }
    }

    private static X509Certificate2Collection LoadChainWithout(
        X509Certificate2 leaf,
        string certificatePath)
    {
        var chain = new X509Certificate2Collection();
        try
        {
            chain.ImportFromPemFile(certificatePath);
        }
        catch
        {
            foreach (var loaded in chain)
                loaded.Dispose();
            throw;
        }

        for (var index = chain.Count - 1; index >= 0; index--)
        {
            var duplicate = chain[index];
            if (!string.Equals(duplicate.Thumbprint, leaf.Thumbprint, StringComparison.OrdinalIgnoreCase))
                continue;

            chain.RemoveAt(index);
            duplicate.Dispose();
        }

        return chain;
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
