// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography.X509Certificates;

namespace Canton.Ledger.Kernel.Security;

/// <summary>
/// Transport-neutral description of the TLS material a Canton client presents as its own
/// identity and accepts as trust anchors. Two independent slots are configured here — the
/// client certificate and the certificate authorities — and each slot accepts either a
/// file-path form (so it binds from <c>IConfiguration</c>) or an already-loaded instance form,
/// never both.
/// </summary>
/// <remarks>
/// <para>
/// Every member that carries TLS material defaults to <see langword="null"/>, so a default-constructed instance
/// leaves <see cref="IsConfigured"/> at <see langword="false"/> and a host can skip registering any
/// TLS material at all.
/// </para>
/// <para>
/// Implements <see cref="IValidatableObject"/> so a host wiring these options through
/// <c>ValidateDataAnnotations().ValidateOnStart()</c> rejects an incoherent combination at
/// startup rather than at the first connection. The rules enforced are:
/// </para>
/// <list type="bullet">
/// <item><description>
/// At most one client identity source: the PEM path form, the PKCS#12 path form, or
/// <see cref="ClientCertificate"/>.
/// </description></item>
/// <item><description>
/// <see cref="ClientCertificateKeyPemPath"/> requires <see cref="ClientCertificatePemPath"/> —
/// a key on its own names no certificate.
/// </description></item>
/// <item><description>
/// <see cref="ClientCertificatePkcs12Password"/> requires
/// <see cref="ClientCertificatePkcs12Path"/> — a password on its own protects nothing.
/// </description></item>
/// <item><description>
/// At most one certificate authority source:
/// <see cref="CertificateAuthorityBundlePemPath"/> or <see cref="CertificateAuthorities"/>.
/// </description></item>
/// <item><description>
/// <see cref="CertificateAuthorities"/>, when supplied, holds at least one certificate — an
/// empty custom trust store rejects every peer.
/// </description></item>
/// <item><description>
/// <see cref="RevocationMode"/>, when it is not <see cref="X509RevocationMode.NoCheck"/>,
/// requires a certificate authority source — revocation checking rides the chain policy built
/// only for custom root trust.
/// </description></item>
/// </list>
/// <para>
/// Path existence is deliberately not checked here: validation performs no I/O. A PEM path that
/// does not resolve surfaces as a <see cref="System.IO.FileNotFoundException"/> from
/// <see cref="SslClientAuthenticationOptionsFactory.Create"/>. An unresolvable
/// <see cref="ClientCertificatePkcs12Path"/> surfaces instead as a
/// <see cref="System.Security.Cryptography.CryptographicException"/> wrapping that
/// <see cref="System.IO.FileNotFoundException"/>, because <see cref="X509CertificateLoader"/>
/// reports a missing PKCS#12 file as a cryptographic failure.
/// </para>
/// <para>
/// Certificate store lookup by thumbprint or subject, and per-handshake selection or rotation
/// callbacks, are out of scope by design.
/// </para>
/// </remarks>
public sealed record TlsOptions : IValidatableObject
{
    /// <summary>
    /// Path to the PEM file holding the client certificate. When
    /// <see cref="ClientCertificateKeyPemPath"/> is <see langword="null"/> this same file is also
    /// read for the private key, which covers the combined certificate-and-key PEM.
    /// </summary>
    public string? ClientCertificatePemPath { get; init; }

    /// <summary>
    /// Path to the PEM file holding the client certificate's private key, when the key lives in a
    /// file of its own. Requires <see cref="ClientCertificatePemPath"/>.
    /// </summary>
    public string? ClientCertificateKeyPemPath { get; init; }

    /// <summary>
    /// Path to the PKCS#12 (<c>.pfx</c> / <c>.p12</c>) file holding the client certificate and its
    /// private key.
    /// </summary>
    public string? ClientCertificatePkcs12Path { get; init; }

    /// <summary>
    /// Password protecting <see cref="ClientCertificatePkcs12Path"/>. Leave <see langword="null"/>
    /// for an unprotected file. Requires <see cref="ClientCertificatePkcs12Path"/>.
    /// </summary>
    public string? ClientCertificatePkcs12Password { get; init; }

    /// <summary>
    /// An already-loaded client certificate, for a host that sources its own material rather than
    /// pointing at a file. Must carry a private key to be usable for client authentication. The
    /// certificate stays owned by the caller: it must outlive every client built from these
    /// options, and the caller disposes it.
    /// </summary>
    public X509Certificate2? ClientCertificate { get; init; }

    /// <summary>
    /// Path to a PEM bundle of certificate authorities that replaces the operating system trust
    /// store for this client rather than adding to it. The authorities it holds become the only
    /// roots trusted for the connection.
    /// </summary>
    public string? CertificateAuthorityBundlePemPath { get; init; }

    /// <summary>
    /// An already-loaded collection of certificate authorities that replaces the operating system
    /// trust store for this client rather than adding to it, for a host that sources its own roots
    /// rather than pointing at a file.
    /// </summary>
    public X509Certificate2Collection? CertificateAuthorities { get; init; }

    /// <summary>
    /// How the peer certificate's revocation status is checked. Default:
    /// <see cref="X509RevocationMode.NoCheck"/>. Governs only when certificate
    /// authorities are configured, because it is carried on the chain policy that
    /// <see cref="X509ChainTrustMode.CustomRootTrust"/> requires; a value other than the default
    /// with no authorities configured is rejected by <see cref="Validate"/> rather than left
    /// silently inert.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Once a chain policy is present, .NET reads revocation from its
    /// <see cref="X509ChainPolicy.RevocationMode"/> and stops consulting
    /// <see cref="System.Net.Security.SslClientAuthenticationOptions.CertificateRevocationCheckMode"/>,
    /// so a host that sets that member itself is silently overridden whenever certificate
    /// authorities are configured. Microsoft does not document that override, which is why this
    /// member exists: it is the only reachable way to ask for revocation checking on a connection
    /// using custom root trust.
    /// </para>
    /// <para>
    /// <see cref="X509RevocationMode.NoCheck"/> is preferred to
    /// <see cref="X509ChainPolicy"/>'s own default of <see cref="X509RevocationMode.Online"/>
    /// because the authorities configured here are typically a private CA publishing no reachable
    /// OCSP or CRL endpoint, against which an online check hangs for the duration of the fetch
    /// attempt on every connection and then fails the handshake anyway.
    /// </para>
    /// </remarks>
    public X509RevocationMode RevocationMode { get; init; } = X509RevocationMode.NoCheck;

    /// <summary>
    /// Whether this instance asks for anything at all — any TLS material, or a
    /// <see cref="RevocationMode"/> other than <see cref="X509RevocationMode.NoCheck"/> — and so
    /// <see langword="false"/> on a default-constructed instance. A host reads this to decide
    /// whether to register anything, instead of wiring options that would change nothing. A
    /// member that names no material on its own, such as <see cref="ClientCertificateKeyPemPath"/>
    /// or <see cref="ClientCertificatePkcs12Password"/>, does not make it
    /// <see langword="true"/>.
    /// </summary>
    public bool IsConfigured =>
        HasClientCertificatePem
        || HasClientCertificatePkcs12
        || ClientCertificate is not null
        || HasCertificateAuthorityBundle
        || CertificateAuthorities is not null
        || RevocationMode != X509RevocationMode.NoCheck;

    internal bool HasClientCertificatePem => !string.IsNullOrWhiteSpace(ClientCertificatePemPath);

    internal bool HasClientCertificatePkcs12 => !string.IsNullOrWhiteSpace(ClientCertificatePkcs12Path);

    internal bool HasCertificateAuthorityBundle => !string.IsNullOrWhiteSpace(CertificateAuthorityBundlePemPath);

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var identitySources = new List<string>();

        if (HasClientCertificatePem)
            identitySources.Add(nameof(ClientCertificatePemPath));

        if (HasClientCertificatePkcs12)
            identitySources.Add(nameof(ClientCertificatePkcs12Path));

        if (ClientCertificate is not null)
            identitySources.Add(nameof(ClientCertificate));

        if (identitySources.Count > 1)
            yield return new ValidationResult(
                "TlsOptions specifies more than one client certificate source; supply exactly one of "
                + $"{nameof(ClientCertificatePemPath)}, {nameof(ClientCertificatePkcs12Path)} or "
                + $"{nameof(ClientCertificate)}.",
                identitySources);

        if (!string.IsNullOrWhiteSpace(ClientCertificateKeyPemPath) && !HasClientCertificatePem)
            yield return new ValidationResult(
                $"TlsOptions.{nameof(ClientCertificateKeyPemPath)} requires "
                + $"{nameof(ClientCertificatePemPath)}.",
                [nameof(ClientCertificateKeyPemPath)]);

        if (!string.IsNullOrWhiteSpace(ClientCertificatePkcs12Password) && !HasClientCertificatePkcs12)
            yield return new ValidationResult(
                $"TlsOptions.{nameof(ClientCertificatePkcs12Password)} requires "
                + $"{nameof(ClientCertificatePkcs12Path)}.",
                [nameof(ClientCertificatePkcs12Password)]);

        if (HasCertificateAuthorityBundle && CertificateAuthorities is not null)
            yield return new ValidationResult(
                "TlsOptions specifies more than one certificate authority source; supply exactly one "
                + $"of {nameof(CertificateAuthorityBundlePemPath)} or {nameof(CertificateAuthorities)}.",
                [nameof(CertificateAuthorityBundlePemPath), nameof(CertificateAuthorities)]);

        if (CertificateAuthorities is { Count: 0 })
            yield return new ValidationResult(
                $"TlsOptions.{nameof(CertificateAuthorities)} is empty; an empty custom trust store "
                + "rejects every peer certificate.",
                [nameof(CertificateAuthorities)]);

        if (RevocationMode != X509RevocationMode.NoCheck
            && !HasCertificateAuthorityBundle
            && CertificateAuthorities is null or { Count: 0 })
            yield return new ValidationResult(
                $"TlsOptions.{nameof(RevocationMode)} requires "
                + $"{nameof(CertificateAuthorityBundlePemPath)} or {nameof(CertificateAuthorities)} — "
                + "it is carried on the chain policy built only for custom root trust, so with no "
                + "authorities configured it checks nothing.",
                [nameof(RevocationMode)]);
    }
}
