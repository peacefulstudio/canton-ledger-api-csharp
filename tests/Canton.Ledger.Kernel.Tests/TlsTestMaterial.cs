// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Canton.Ledger.Kernel.Tests;

internal sealed class TlsTestMaterial : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("canton-tls").FullName;
    private readonly List<X509Certificate2> _certificates = [];

    public X509Certificate2 CreateCertificateAuthority(string commonName)
    {
        using var key = RSA.Create(2048);

        var request = new CertificateRequest(
            $"CN={commonName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, 0, critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));

        return Track(request.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1)));
    }

    public X509Certificate2 CreateSelfSigned(string commonName)
    {
        using var key = RSA.Create(2048);

        var request = new CertificateRequest(
            $"CN={commonName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return Track(request.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1)));
    }

    public X509Certificate2 IssueCertificateAuthority(X509Certificate2 authority, string commonName)
    {
        using var key = RSA.Create(2048);

        var request = new CertificateRequest(
            $"CN={commonName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, 0, critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));

        return Issue(authority, request, key);
    }

    public X509Certificate2 IssueClientCertificate(X509Certificate2 authority, string commonName) =>
        Issue(authority, commonName, X509KeyUsageFlags.DigitalSignature, ClientAuthenticationOid, subjectAlternativeName: null);

    public X509Certificate2 IssueServerCertificate(X509Certificate2 authority, string dnsName) =>
        Issue(authority, dnsName, X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DigitalSignature, ServerAuthenticationOid, dnsName);

    public string WritePemCertificate(string fileName, X509Certificate2 certificate) =>
        WriteText(fileName, certificate.ExportCertificatePem());

    public string WritePemPrivateKey(string fileName, X509Certificate2 certificate)
    {
        using var key = certificate.GetRSAPrivateKey()!;
        return WriteText(fileName, key.ExportPkcs8PrivateKeyPem());
    }

    public string WriteCombinedPem(
        string fileName,
        X509Certificate2 certificate,
        params X509Certificate2[] additionalCertificates)
    {
        using var key = certificate.GetRSAPrivateKey()!;
        var certificates = new[] { certificate }.Concat(additionalCertificates);
        return WriteText(
            fileName,
            string.Join(Environment.NewLine, certificates.Select(value => value.ExportCertificatePem()))
            + Environment.NewLine
            + key.ExportPkcs8PrivateKeyPem());
    }

    public string WritePkcs12(
        string fileName,
        X509Certificate2 certificate,
        string? password,
        params X509Certificate2[] additionalCertificates)
    {
        var path = Path.Combine(_directory, fileName);
        X509Certificate2Collection certificates = [certificate, .. additionalCertificates];
        File.WriteAllBytes(path, certificates.Export(X509ContentType.Pkcs12, password)!);
        return path;
    }

    public string WritePemBundle(string fileName, params X509Certificate2[] certificates) =>
        WriteText(fileName, string.Join(Environment.NewLine, certificates.Select(certificate => certificate.ExportCertificatePem())));

    public string AbsentPath(string fileName) => Path.Combine(_directory, fileName);

    public void Dispose()
    {
        foreach (var certificate in _certificates)
            certificate.Dispose();

        Directory.Delete(_directory, recursive: true);
    }

    private const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";
    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";

    private X509Certificate2 Issue(
        X509Certificate2 authority,
        string commonName,
        X509KeyUsageFlags keyUsage,
        string extendedKeyUsageOid,
        string? subjectAlternativeName)
    {
        using var key = RSA.Create(2048);

        var request = new CertificateRequest(
            $"CN={commonName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(keyUsage, critical: true));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension([new Oid(extendedKeyUsageOid)], critical: false));

        if (subjectAlternativeName is not null)
        {
            var alternativeNames = new SubjectAlternativeNameBuilder();

            if (IPAddress.TryParse(subjectAlternativeName, out var address))
                alternativeNames.AddIpAddress(address);
            else
                alternativeNames.AddDnsName(subjectAlternativeName);

            request.CertificateExtensions.Add(alternativeNames.Build());
        }

        return Issue(authority, request, key);
    }

    private X509Certificate2 Issue(
        X509Certificate2 authority,
        CertificateRequest request,
        RSA key)
    {
        var notAfter = new[]
        {
            DateTimeOffset.UtcNow.AddMinutes(50),
            new DateTimeOffset(authority.NotAfter).AddSeconds(-1)
        }.Min();
        using var issued = request.Create(
            authority,
            DateTimeOffset.UtcNow.AddHours(-1),
            notAfter,
            RandomNumberGenerator.GetBytes(16));

        using var issuedWithPrivateKey = issued.CopyWithPrivateKey(key);
        return Track(X509CertificateLoader.LoadPkcs12(
            issuedWithPrivateKey.Export(X509ContentType.Pkcs12),
            password: null,
            X509KeyStorageFlags.Exportable));
    }

    private string WriteText(string fileName, string contents)
    {
        var path = Path.Combine(_directory, fileName);
        File.WriteAllText(path, contents);
        return path;
    }

    private X509Certificate2 Track(X509Certificate2 certificate)
    {
        _certificates.Add(certificate);
        return certificate;
    }
}
