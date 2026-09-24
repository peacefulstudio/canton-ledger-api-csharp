// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.Json.Serialization;

namespace Canton.Ledger.Benchmarks;

internal sealed record BenchmarkOptions
{
    public int WarmupCommands { get; init; } = 20;

    public int SeedContractsPerTransport { get; init; } = 500;

    public int Concurrency { get; init; } = 16;

    public int Repetitions { get; init; } = 5;

    public int LatencySamples { get; init; } = 200;

    public int TailSamples { get; init; } = 50;

    public int TailSpacingMs { get; init; } = 500;

    public IReadOnlyList<int> RestIdleWindowsMs { get; init; } = [250];

    public int AcsContracts { get; init; } = 200;

    public int PqsLagSamples { get; init; } = 50;

    public int PqsRows { get; init; } = 500;

    [JsonIgnore]
    public string OutputDirectory { get; init; } = "benchmark-results";

    public string Commit { get; init; } = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "unknown";

    public string Runner { get; init; } = RunnerFromEnvironment();

    public static BenchmarkOptions Parse(IReadOnlyList<string> args)
    {
        var options = new BenchmarkOptions();
        for (var i = 0; i < args.Count; i += 2)
        {
            if (i + 1 >= args.Count)
            {
                throw new ArgumentException($"option '{args[i]}' needs a value");
            }

            var value = args[i + 1];
            options = args[i] switch
            {
                "--warmup" => options with { WarmupCommands = Int(value) },
                "--seed-contracts" => options with { SeedContractsPerTransport = Int(value) },
                "--concurrency" => options with { Concurrency = Int(value) },
                "--repetitions" => options with { Repetitions = Int(value) },
                "--latency-samples" => options with { LatencySamples = Int(value) },
                "--tail-samples" => options with { TailSamples = Int(value) },
                "--tail-spacing-ms" => options with { TailSpacingMs = Int(value) },
                "--rest-idle-windows-ms" => options with { RestIdleWindowsMs = IntList(value) },
                "--acs-contracts" => options with { AcsContracts = Int(value) },
                "--pqs-lag-samples" => options with { PqsLagSamples = Int(value) },
                "--pqs-rows" => options with { PqsRows = Int(value) },
                "--output" => options with { OutputDirectory = value },
                "--commit" => options with { Commit = value },
                "--runner" => options with { Runner = value },
                _ => throw new ArgumentException($"unknown option '{args[i]}'"),
            };
        }

        return options;
    }

    private static int Int(string value) => int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);

    private static int[] IntList(string value) =>
        value.Length == 0 ? [] : value.Split(',').Select(Int).ToArray();

    private static string RunnerFromEnvironment() =>
        Environment.GetEnvironmentVariable("RUNNER_NAME") is { Length: > 0 } runner
            ? $"GitHub Actions ({runner})"
            : "local";
}
