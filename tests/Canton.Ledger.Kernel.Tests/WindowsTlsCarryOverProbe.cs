#pragma warning disable
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Canton.Ledger.Kernel.Security;
using Xunit;

namespace Canton.Ledger.Kernel.Tests;

public sealed class WindowsTlsCarryOverProbe
{
    [Fact] public Task R01_instance_then_fresh_material() => Scenario("R01", "instance");
    [Fact] public Task R02_pem_then_fresh_material() => Scenario("R02", "pem");
    [Fact] public Task R03_combined_pem_then_fresh_material() => Scenario("R03", "combined");
    [Fact] public Task R04_pkcs12_then_fresh_material() => Scenario("R04", "pkcs12");
    [Fact] public Task R05_pkcs12_open_then_fresh_material() => Scenario("R05", "pkcs12-open");
    [Fact] public Task R06_instance_same_material() => Scenario("R06", "instance", sameMaterial: true);
    [Fact] public Task R07_instance_first_material_kept_alive() => Scenario("R07", "instance", keepFirstAlive: true);
    [Fact] public Task R08_instance_second_host_differs() => Scenario("R08", "instance", secondHost: "probe-b.local");
    [Fact] public Task R09_instance_default_protocols() => Scenario("R09", "instance", protocols: SslProtocols.None);
    [Fact] public Task R10_instance_client_AllowTlsResume_false() => Scenario("R10", "instance", secondClient: o => o.AllowTlsResume = false);
    [Fact] public Task R11_instance_server_AllowTlsResume_false() => Scenario("R11", "instance", secondServer: o => o.AllowTlsResume = false);
    [Fact] public Task R12_instance_both_AllowTlsResume_false_on_every_handshake() => Scenario("R12", "instance", secondClient: o => o.AllowTlsResume = false, secondServer: o => o.AllowTlsResume = false, firstClient: o => o.AllowTlsResume = false, firstServer: o => o.AllowTlsResume = false);
    [Fact] public Task R13_instance_second_selection_callback_null() => Scenario("R13", "instance", secondClient: o => o.LocalCertificateSelectionCallback = (s, t, l, r, i) => null!);
    [Fact] public Task R14_instance_no_cert_three_times() => Scenario("R14", "instance", noCertRepeats: 3);
    [Fact] public Task R15_no_presenter_control_three_no_cert() => Scenario("R15", null, noCertRepeats: 3);
    [Fact] public Task R16_rejected_no_cert_handshake_first_then_present_then_no_cert() => Scenario("R16", "instance", preface: "reject");
    [Fact] public Task R17_successful_no_cert_handshake_first_then_present_then_no_cert() => Scenario("R17", "instance", preface: "success");
    [Fact] public Task R18_instance_then_no_cert_with_empty_ClientCertificates() => Scenario("R18", "instance", secondClient: o => o.ClientCertificates = new X509CertificateCollection());

    private static async Task Scenario(
        string label,
        string? presenterKind,
        bool sameMaterial = false,
        bool keepFirstAlive = false,
        string secondHost = "localhost",
        SslProtocols protocols = SslProtocols.Tls12,
        Action<SslClientAuthenticationOptions>? firstClient = null,
        Action<SslServerAuthenticationOptions>? firstServer = null,
        Action<SslClientAuthenticationOptions>? secondClient = null,
        Action<SslServerAuthenticationOptions>? secondServer = null,
        int noCertRepeats = 1,
        string? preface = null)
    {
        var materialA = new TlsTestMaterial();
        var caA = materialA.CreateCertificateAuthority("Canton Probe Root A");
        var serverA = materialA.IssueServerCertificate(caA, sameMaterial ? secondHost : "localhost");
        var clientA = materialA.IssueClientCertificate(caA, "Canton Probe Client A");
        var clientAThumbprint = clientA.Thumbprint;
        TlsProbeLog.Line($"{label} presenter={presenterKind ?? "none"} clientA={clientAThumbprint}");

        try
        {
            if (preface is not null)
            {
                var prefaceAuthority = preface == "reject" ? materialA.CreateCertificateAuthority("Canton Probe Unrelated") : caA;
                var prefaceSeen = await Once(label, "preface-" + preface, caA, serverA, new TlsOptions { CertificateAuthorities = [prefaceAuthority] }, sameMaterial ? secondHost : "localhost", protocols, null, null);
                TlsProbeLog.Line($"{label} preface-{preface} serverSaw={TlsProbeLog.Describe(prefaceSeen)}");
            }

            if (presenterKind is not null)
            {
                var presenterOptions = PresenterOptions(materialA, caA, clientA, presenterKind);
                var first = await Once(label, "present", caA, serverA, presenterOptions, sameMaterial ? secondHost : "localhost", protocols, firstClient, firstServer);
                TlsProbeLog.Line($"{label} present-step serverSaw={TlsProbeLog.Describe(first)} presentedIsClientA={string.Equals(first, clientAThumbprint, StringComparison.OrdinalIgnoreCase)}");
            }

            TlsProbeLog.Line($"{label} store {TlsProbeLog.StoreSnapshot()}");

            TlsTestMaterial? materialB = null;
            var caB = caA;
            var serverB = serverA;
            if (!sameMaterial)
            {
                if (!keepFirstAlive && presenterKind is not null)
                {
                    materialA.Dispose();
                    materialA = null!;
                }

                materialB = new TlsTestMaterial();
                caB = materialB.CreateCertificateAuthority("Canton Probe Root B");
                serverB = materialB.IssueServerCertificate(caB, secondHost);
                materialB.IssueClientCertificate(caB, "Canton Probe Client B");
            }

            try
            {
                for (var i = 0; i < noCertRepeats; i++)
                {
                    var seen = await Once(label, $"no-cert#{i}", caB, serverB, new TlsOptions { CertificateAuthorities = [caB] }, secondHost, protocols, secondClient, secondServer);
                    var verdict = seen is null ? "NO-CLIENT-CERT" : "LEAK";
                    TlsProbeLog.Line($"{label} RESULT#{i} {verdict} serverSaw={TlsProbeLog.Describe(seen)} equalsClientA={string.Equals(seen, clientAThumbprint, StringComparison.OrdinalIgnoreCase)}");
                }
            }
            finally
            {
                materialB?.Dispose();
            }
        }
        finally
        {
            materialA?.Dispose();
        }
    }

    private static TlsOptions PresenterOptions(TlsTestMaterial material, X509Certificate2 ca, X509Certificate2 client, string kind) => kind switch
    {
        "instance" => new TlsOptions { ClientCertificate = client, CertificateAuthorities = [ca] },
        "pem" => new TlsOptions
        {
            ClientCertificatePemPath = material.WritePemCertificate("client.crt", client),
            ClientCertificateKeyPemPath = material.WritePemPrivateKey("client.key", client),
            CertificateAuthorities = [ca]
        },
        "combined" => new TlsOptions { ClientCertificatePemPath = material.WriteCombinedPem("client-combined.pem", client), CertificateAuthorities = [ca] },
        "pkcs12" => new TlsOptions { ClientCertificatePkcs12Path = material.WritePkcs12("client.pfx", client, "hunter2"), ClientCertificatePkcs12Password = "hunter2", CertificateAuthorities = [ca] },
        "pkcs12-open" => new TlsOptions { ClientCertificatePkcs12Path = material.WritePkcs12("client-open.pfx", client, null), CertificateAuthorities = [ca] },
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static async Task<string?> Once(
        string label,
        string step,
        X509Certificate2 ca,
        X509Certificate2 server,
        TlsOptions tlsOptions,
        string host,
        SslProtocols protocols,
        Action<SslClientAuthenticationOptions>? clientTweak,
        Action<SslServerAuthenticationOptions>? serverTweak)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var accept = Task.Run(async () =>
        {
            using var accepted = await listener.AcceptTcpClientAsync();
            await using var stream = new SslStream(accepted.GetStream(), false);
            var serverOptions = new SslServerAuthenticationOptions
            {
                ServerCertificate = server,
                ClientCertificateRequired = true,
                EnabledSslProtocols = protocols,
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            };
            serverTweak?.Invoke(serverOptions);
            await stream.AuthenticateAsServerAsync(serverOptions);
            return stream.RemoteCertificate?.GetCertHashString();
        });

        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
            var options = SslClientAuthenticationOptionsFactory.Create(tlsOptions);
            options.TargetHost = host;
            options.EnabledSslProtocols = protocols;
            clientTweak?.Invoke(options);
            await using var stream = new SslStream(tcp.GetStream(), false);
            await stream.AuthenticateAsClientAsync(options);
            TlsProbeLog.Line($"{label} {step} client-side protocol={stream.SslProtocol} localCertificate={stream.LocalCertificate?.GetCertHashString() ?? "null"} mutual={stream.IsMutuallyAuthenticated}");
            return await accept;
        }
        catch (Exception e)
        {
            TlsProbeLog.Line($"{label} {step} ERROR {e.GetType().Name}: {e.Message}");
            return null;
        }
        finally
        {
            listener.Stop();
        }
    }
}
