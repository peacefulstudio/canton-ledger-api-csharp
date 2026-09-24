// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;

namespace Canton.Ledger.Benchmarks;

internal static class Seed
{
    public const decimal AssetAmount = 1m;

    private const int MaxRetries = 9;

    private static int _retries;

    public static int Retries => Volatile.Read(ref _retries);

    public static Task<ContractId<Marker>> CreateMarkerAsync(
        ILedgerWriter writer, Party owner, CancellationToken cancellationToken) =>
        CreateAsync(writer, new Marker(owner), owner, cancellationToken);

    public static Task<ContractId<Asset>> CreateAssetAsync(
        ILedgerWriter writer, Party issuer, CancellationToken cancellationToken) =>
        CreateAsync(writer, new Asset(issuer, AssetAmount), issuer, cancellationToken);

    private static async Task<ContractId<T>> CreateAsync<T>(
        ILedgerWriter writer, T template, Party actAs, CancellationToken cancellationToken)
        where T : Daml.Runtime.IDamlType, ITemplate
    {
        for (var attempt = 0; ; attempt++)
        {
            var outcome = await writer.TryCreateAsync(template, actAs, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            switch (outcome)
            {
                case ExerciseOutcome<ContractId<T>>.One created:
                    return created.Result;
                case ExerciseOutcome<ContractId<T>>.DamlError error when IsRetryable(error.Category) && attempt < MaxRetries:
                    Interlocked.Increment(ref _retries);
                    await Task.Delay(TimeSpan.FromMilliseconds(250 << attempt), cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    throw new InvalidOperationException($"create did not succeed: {outcome}");
            }
        }
    }

    private static bool IsRetryable(DamlErrorCategory category) =>
        category is DamlErrorCategory.ContentionOnSharedResources or DamlErrorCategory.TransientServerFailure;
}
