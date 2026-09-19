#pragma warning disable
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace Canton.Ledger.Kernel.Tests;

public sealed class WindowsTlsProbe
{
    private const string ServerHostName = "localhost";

    [Fact]
    public void Env_info()
    {
        TlsProbeLog.Line($"ENV OSDescription={RuntimeInformation.OSDescription} OSArch={RuntimeInformation.OSArchitecture} ProcArch={RuntimeInformation.ProcessArchitecture} Framework={RuntimeInformation.FrameworkDescription} OSVersion={Environment.OSVersion} User={Environment.UserName} UserDomain={Environment.UserDomainName}");
        TlsProbeLog.Line($"ENV store {TlsProbeLog.StoreSnapshot()}");
    }

    [Fact] public Task V00_no_client_cert_exists_anywhere() => Run("V00_no_client_cert_exists", client: null);

    [Fact] public Task V01_client_cert_TlsTestMaterial_default_import() => Run("V01_TlsTestMaterial_client", client: (m, ca) => m.IssueClientCertificate(ca, "Canton Probe Client V01"));

    [Fact] public Task V02_client_cert_issued_then_disposed_before_handshake() => Run("V02_disposed_before", client: (m, ca) => IssueClient(ca, "V02", X509KeyStorageFlags.Exportable), disposeClientBeforeHandshake: true);

    [Fact] public Task V03_client_Exportable_EphemeralKeySet() => Run("V03_Exportable_Ephemeral", client: (m, ca) => IssueClient(ca, "V03", X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet), windowsOnly: true);

    [Fact] public Task V04_client_Exportable_PersistKeySet() => Run("V04_Exportable_PersistKeySet", client: (m, ca) => IssueClient(ca, "V04", X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet), windowsOnly: true);

    [Fact] public Task V05_client_Exportable_MachineKeySet() => Run("V05_Exportable_MachineKeySet", client: (m, ca) => IssueClient(ca, "V05", X509KeyStorageFlags.Exportable | X509KeyStorageFlags.MachineKeySet), windowsOnly: true);

    [Fact] public Task V06_client_default_flags() => Run("V06_default_flags", client: (m, ca) => IssueClient(ca, "V06", X509KeyStorageFlags.DefaultKeySet));

    [Fact] public Task V07_client_CopyWithPrivateKey_ephemeral() => Run("V07_CopyWithPrivateKey", client: (m, ca) => IssueClient(ca, "V07", flags: null));

    [Fact]
    public Task V08_baseline_plus_null_selection_callback() => Run(
        "V08_selection_callback_null",
        client: (m, ca) => m.IssueClientCertificate(ca, "Canton Probe Client V08"),
        tweak: options => options.LocalCertificateSelectionCallback = (sender, target, local, remote, issuers) =>
        {
            TlsProbeLog.Line($"V08 selection callback invoked local={local.Count} issuers={issuers.Length} remote={remote?.Subject}");
            return null!;
        });

    [Fact]
    public Task V09_baseline_plus_empty_ClientCertificates() => Run(
        "V09_empty_ClientCertificates",
        client: (m, ca) => m.IssueClientCertificate(ca, "Canton Probe Client V09"),
        tweak: options => options.ClientCertificates = new X509CertificateCollection());

    [Fact]
    public async Task V10_baseline_three_times_in_one_process()
    {
        for (var i = 0; i < 3; i++)
            await Run($"V10_repeat_{i}", client: (m, ca) => m.IssueClientCertificate(ca, $"Canton Probe Client V10-{i}"));
    }

    [Fact]
    public Task V11_baseline_default_protocols_not_forced_to_Tls12() => Run(
        "V11_default_protocols",
        client: (m, ca) => m.IssueClientCertificate(ca, "Canton Probe Client V11"),
        protocols: SslProtocols.None);

    [Fact]
    public Task V12_no_client_cert_default_protocols() => Run("V12_no_client_default_protocols", client: null, protocols: SslProtocols.None);

    [Fact]
    public Task V13_baseline_plus_ClientCertificateContext_of_unrelated_cert_control() => Run(
        "V13_control_context_with_other_cert",
        client: (m, ca) => m.IssueClientCertificate(ca, "Canton Probe Client V13"),
        tweak: null,
        contextClient: true);

    private static X509Certificate2 IssueClient(X509Certificate2 authority, string label, X509KeyStorageFlags? flags)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN=Canton Probe Client {label}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.2")], false));

        using var issued = request.Create(authority, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddMinutes(50), RandomNumberGenerator.GetBytes(16));
        if (flags is null)
            return issued.CopyWithPrivateKey(key);
        using var withKey = issued.CopyWithPrivateKey(key);
        return X509CertificateLoader.LoadPkcs12(withKey.Export(X509ContentType.Pkcs12), null, flags.Value);
    }

    private static async Task Run(
        string label,
        Func<TlsTestMaterial, X509Certificate2, X509Certificate2>? client,
        Action<SslClientAuthenticationOptions>? tweak = null,
        SslProtocols protocols = SslProtocols.Tls12,
        bool disposeClientBeforeHandshake = false,
        bool contextClient = false,
        bool windowsOnly = false)
    {
        if (windowsOnly && !OperatingSystem.IsWindows())
        {
            TlsProbeLog.Line($"{label} SKIPPED (variant persists or imports keys into the OS user store; Windows only)");
            return;
        }

        using var material = new TlsTestMaterial();
        var owner = $"{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(material)}";
        var ca = material.CreateCertificateAuthority($"Canton Probe Root {label}");
        var server = material.IssueServerCertificate(ca, ServerHostName);

        X509Certificate2? clientCertificate = null;
        string? ownThumbprint = null;
        try
        {
            if (client is not null)
            {
                try
                {
                    clientCertificate = client(material, ca);
                }
                catch (Exception e)
                {
                    TlsProbeLog.Line($"{label} CLIENT-CERT-CREATION-FAILED {e.GetType().Name}: {e.Message}");
                    return;
                }

                ownThumbprint = clientCertificate.Thumbprint;
                TlsProbeLog.Issue(owner, "probe-client:" + label, clientCertificate);
                if (disposeClientBeforeHandshake)
                {
                    clientCertificate.Dispose();
                    TlsProbeLog.Line($"{label} client cert disposed before handshake");
                }
            }

            TlsProbeLog.Line($"{label} START ownClient={ownThumbprint ?? "none"} store {TlsProbeLog.StoreSnapshot()}");

            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var accept = AcceptAsync(listener, server, protocols);

            string? clientLocal = null;
            bool? mutual = null;
            string? clientError = null;
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync((IPEndPoint)listener.LocalEndpoint);

                var options = new SslClientAuthenticationOptions
                {
                    TargetHost = ServerHostName,
                    EnabledSslProtocols = protocols,
                    CertificateChainPolicy = CustomRoot(ca)
                };
                if (contextClient && clientCertificate is not null)
                    options.ClientCertificateContext = SslStreamCertificateContext.Create(clientCertificate, null);
                tweak?.Invoke(options);

                await using var stream = new SslStream(tcp.GetStream(), false);
                await stream.AuthenticateAsClientAsync(options);
                clientLocal = stream.LocalCertificate?.GetCertHashString();
                mutual = stream.IsMutuallyAuthenticated;
                TlsProbeLog.Line($"{label} client-side protocol={stream.SslProtocol} localCertificate={clientLocal ?? "null"} mutual={mutual}");
            }
            catch (Exception e)
            {
                clientError = e.GetType().Name + ": " + e.Message;
            }

            string? serverSaw = null;
            string? serverError = null;
            try
            {
                serverSaw = await accept;
            }
            catch (Exception e)
            {
                serverError = e.GetType().Name + ": " + e.Message;
            }
            listener.Stop();

            var verdict = serverSaw is null ? "NO-CLIENT-CERT" : "LEAK";
            TlsProbeLog.Line($"{label} RESULT {verdict} serverSaw={TlsProbeLog.Describe(serverSaw)} clientError={clientError ?? "none"} serverError={serverError ?? "none"} ownIsSeen={(ownThumbprint is not null && string.Equals(serverSaw, ownThumbprint, StringComparison.OrdinalIgnoreCase))} store {TlsProbeLog.StoreSnapshot()}");
        }
        finally
        {
            if (!disposeClientBeforeHandshake)
                clientCertificate?.Dispose();
        }
    }

    private static X509ChainPolicy CustomRoot(X509Certificate2 authority)
    {
        var policy = new X509ChainPolicy { TrustMode = X509ChainTrustMode.CustomRootTrust, RevocationMode = X509RevocationMode.NoCheck };
        policy.CustomTrustStore.Add(authority);
        return policy;
    }

    private static async Task<string?> AcceptAsync(TcpListener listener, X509Certificate2 serverCertificate, SslProtocols protocols)
    {
        using var accepted = await listener.AcceptTcpClientAsync();
        await using var stream = new SslStream(accepted.GetStream(), false);
        await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        {
            ServerCertificate = serverCertificate,
            ClientCertificateRequired = true,
            EnabledSslProtocols = protocols,
            RemoteCertificateValidationCallback = (_, _, _, _) => true
        });
        return stream.RemoteCertificate?.GetCertHashString();
    }
}
