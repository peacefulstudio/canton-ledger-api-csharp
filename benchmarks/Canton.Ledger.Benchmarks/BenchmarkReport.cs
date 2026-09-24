// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Canton.Ledger.Benchmarks;

internal sealed record LatencyResult(string Scenario, string Transport, LatencySummary Summary);

internal sealed record ThroughputResult(string Scenario, string Transport, string Unit, ThroughputSummary Summary);

internal sealed record BenchmarkReport(
    BenchmarkEnvironment Environment,
    BenchmarkOptions Parameters,
    IReadOnlyList<LatencyResult> Latencies,
    IReadOnlyList<ThroughputResult> Throughputs,
    IReadOnlyList<string> Notes)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<(string Json, string Markdown)> WriteAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Parameters.OutputDirectory);
        var jsonPath = Path.Combine(Parameters.OutputDirectory, "results.json");
        var markdownPath = Path.Combine(Parameters.OutputDirectory, "results.md");
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(this, JsonOptions), cancellationToken)
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(markdownPath, ToMarkdown(), cancellationToken).ConfigureAwait(false);
        return (jsonPath, markdownPath);
    }

    public string ToMarkdown()
    {
        var markdown = new StringBuilder();
        markdown.AppendLine("## Environment").AppendLine()
            .AppendLine("| | |").AppendLine("| --- | --- |")
            .AppendLine(Row("Started (UTC)", Environment.StartedAt.ToString("u", CultureInfo.InvariantCulture)))
            .AppendLine(Row("Commit", $"`{Environment.Commit}`"))
            .AppendLine(Row("Runner", Environment.Runner))
            .AppendLine(Row("OS / arch / CPUs", $"{Environment.OperatingSystem} / {Environment.Architecture} / {Environment.ProcessorCount}"))
            .AppendLine(Row(".NET runtime", Environment.DotnetRuntime))
            .AppendLine(Row("Client packages", Environment.ClientVersion))
            .AppendLine(Row("Canton protos pinned", Environment.CantonProtoVersion))
            .AppendLine(Row("Participant Ledger API version", Environment.LedgerApiVersion))
            .AppendLine(Row("PQS measured", Environment.PqsMeasured ? "yes" : "no"))
            .AppendLine();

        markdown.AppendLine("## Latency (milliseconds)").AppendLine()
            .AppendLine("| Scenario | Transport | Samples | p50 | p95 | p99 | Max | Mean |")
            .AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var result in Latencies)
        {
            var summary = result.Summary;
            markdown.AppendLine(CultureInfo.InvariantCulture,
                $"| {result.Scenario} | {result.Transport} | {summary.Samples} | {summary.P50Ms:F1} | {summary.P95Ms:F1} | {summary.P99Ms:F1} | {summary.MaxMs:F1} | {summary.MeanMs:F1} |");
        }

        markdown.AppendLine().AppendLine("## Throughput").AppendLine()
            .AppendLine("| Scenario | Transport | Items | Repetitions | Median seconds | Median rate | Best rate |")
            .AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: |");
        foreach (var result in Throughputs)
        {
            var summary = result.Summary;
            markdown.AppendLine(CultureInfo.InvariantCulture,
                $"| {result.Scenario} | {result.Transport} | {summary.Items} | {summary.Repetitions} | {summary.MedianSeconds:F3} | {summary.MedianItemsPerSecond:F0} {result.Unit} | {summary.BestItemsPerSecond:F0} {result.Unit} |");
        }

        if (Notes.Count > 0)
        {
            markdown.AppendLine().AppendLine("## Run notes").AppendLine();
            foreach (var note in Notes)
            {
                markdown.AppendLine(CultureInfo.InvariantCulture, $"- {note}");
            }
        }

        return markdown.ToString();
    }

    private static string Row(string label, string value) => $"| {label} | {value} |";
}
