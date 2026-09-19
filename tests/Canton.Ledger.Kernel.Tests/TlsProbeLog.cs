#pragma warning disable
using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;

namespace Canton.Ledger.Kernel.Tests;

internal static class TlsProbeLog
{
    private static readonly string LogPath =
        Environment.GetEnvironmentVariable("TLS_PROBE_LOG") ?? Path.Combine(Path.GetTempPath(), "tls-probe.log");
    private static readonly object Gate = new();
    private static int _sequence;

    public static readonly ConcurrentQueue<(string Thumbprint, string Subject, string Owner, string Kind)> Issued = new();

    public static void Line(string text)
    {
        lock (Gate)
        {
            var line = $"[{Interlocked.Increment(ref _sequence):D4} pid={Environment.ProcessId} t={Environment.CurrentManagedThreadId}] {text}";
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
    }

    public static void Issue(string owner, string kind, X509Certificate2 certificate)
    {
        Issued.Enqueue((certificate.Thumbprint, certificate.Subject, owner, kind));
        Line($"ISSUED owner={owner} kind={kind} subject={certificate.Subject} thumb={certificate.Thumbprint} hasKey={certificate.HasPrivateKey}");
    }

    public static string Describe(string? thumbprint)
    {
        if (thumbprint is null) return "null";
        var hit = Issued.Where(i => string.Equals(i.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase)).ToList();
        return hit.Count == 0
            ? $"{thumbprint} (NOT in issued registry)"
            : $"{thumbprint} (issued: {string.Join("; ", hit.Select(h => $"{h.Kind} owner={h.Owner} {h.Subject}"))})";
    }

    public static string StoreSnapshot()
    {
        var parts = new List<string>();
        foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            try
            {
                using var store = new X509Store(StoreName.My, location);
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                parts.Add($"{location}\\My count={store.Certificates.Count} [{string.Join(",", store.Certificates.Select(c => c.Thumbprint[..8] + (c.HasPrivateKey ? "+k" : "")))}]");
            }
            catch (Exception e)
            {
                parts.Add($"{location}\\My unreadable: {e.GetType().Name}");
            }
        }
        return string.Join(" | ", parts);
    }
}
