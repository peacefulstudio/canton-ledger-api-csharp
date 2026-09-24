// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Benchmarks;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

var options = BenchmarkOptions.Parse(args);
await using var ledger = await BenchmarkLedger.OpenAsync(options, cancellation.Token);
var report = await BenchmarkSuite.RunAsync(ledger, options, cancellation.Token);
var (json, markdown) = await report.WriteAsync(cancellation.Token);
Console.WriteLine(report.ToMarkdown());
Console.Error.WriteLine($"wrote {json} and {markdown}");
