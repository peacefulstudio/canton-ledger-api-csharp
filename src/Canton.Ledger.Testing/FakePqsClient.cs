// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Canton.Ledger.Testing;

/// <summary>
/// An in-memory <see cref="IPqsClient"/> test double that replays canned query results staged,
/// per Daml type, through the fluent <see cref="FakePqsClientBuilder"/>. It lets business logic
/// that queries PQS be unit-tested without a live PostgreSQL-backed PQS instance and without a
/// mocking framework.
/// </summary>
/// <remarks>
/// The filtered overloads (<see cref="QueryAsync{T}(PqsFilter, CancellationToken)"/> and
/// <see cref="QueryOneAsync{T}"/>) ignore the filter's content and return the first staged
/// result(s) regardless of what the filter would actually match against a live PQS:
/// <see cref="PqsFilter"/>'s SQL-translation internals (<c>ToSqlClause</c>) are <c>internal</c> to
/// <c>Canton.Ledger.Abstractions</c> and not visible here, and re-implementing filter evaluation
/// in-memory is out of scope for this fake — stage the exact result set your test expects for the
/// filter you exercise instead. This is more surprising for <see cref="QueryOneAsync{T}"/> than for
/// the bulk overload: the real contract promises <c>null</c> when nothing matches, but this fake
/// always returns the first staged contract as long as one was staged, even if a live PQS query with
/// that filter would have matched none of them. Any Daml type or interface that was not staged throws
/// a descriptive <see cref="NotSupportedException"/> naming the missing setup, so a test never
/// silently exercises unconfigured behaviour. Construct instances through <see cref="Create"/>.
/// </remarks>
public sealed class FakePqsClient : IPqsClient
{
    private readonly IReadOnlyDictionary<Type, object> _templateResults;
    private readonly IReadOnlyDictionary<Type, object> _interfaceResults;

    internal FakePqsClient(
        IReadOnlyDictionary<Type, object> templateResults,
        IReadOnlyDictionary<Type, object> interfaceResults)
    {
        _templateResults = templateResults;
        _interfaceResults = interfaceResults;
    }

    /// <summary>Starts a new fluent builder for a <see cref="FakePqsClient"/>.</summary>
    /// <returns>An empty builder; stage query results on it, then call <see cref="FakePqsClientBuilder.Build"/>.</returns>
    public static FakePqsClientBuilder Create() => new();

    /// <inheritdoc />
    public Task<IReadOnlyList<Contract<T>>> QueryAsync<T>(CancellationToken cancellationToken = default)
        where T : ITemplate =>
        Task.FromResult(StagedContracts<T>());

    /// <summary>
    /// Returns the slice of the staged contracts selected by <paramref name="page"/>. The slice is
    /// taken over the staged set in staging order — unlike the real client, the fake does not
    /// re-sort by contract id — so stage contracts in the order pages should surface them.
    /// </summary>
    public Task<IReadOnlyList<Contract<T>>> QueryAsync<T>(PqsPage page, CancellationToken cancellationToken = default)
        where T : ITemplate
    {
        ArgumentNullException.ThrowIfNull(page);
        return Task.FromResult(Slice(StagedContracts<T>(), page));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<InterfaceContract<TInterface, TView>>> QueryAsync<TInterface, TView>(
        CancellationToken cancellationToken = default)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord =>
        Task.FromResult(StagedInterfaceContracts<TInterface, TView>());

    /// <summary>
    /// Returns the slice of the staged interface contracts selected by <paramref name="page"/>.
    /// The slice is taken over the staged set in staging order — unlike the real client, the fake
    /// does not re-sort by contract id — so stage contracts in the order pages should surface them.
    /// </summary>
    public Task<IReadOnlyList<InterfaceContract<TInterface, TView>>> QueryAsync<TInterface, TView>(
        PqsPage page,
        CancellationToken cancellationToken = default)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord
    {
        ArgumentNullException.ThrowIfNull(page);
        return Task.FromResult(Slice(StagedInterfaceContracts<TInterface, TView>(), page));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Contract<T>>> QueryAsync<T>(PqsFilter filter, CancellationToken cancellationToken = default)
        where T : ITemplate
    {
        ArgumentNullException.ThrowIfNull(filter);
        return Task.FromResult(StagedContracts<T>());
    }

    /// <summary>
    /// Returns the slice of the staged contracts selected by <paramref name="page"/>, ignoring the
    /// filter's content like the unpaged filtered overload does. The slice is taken over the staged
    /// set in staging order — unlike the real client, the fake does not re-sort by contract id.
    /// </summary>
    public Task<IReadOnlyList<Contract<T>>> QueryAsync<T>(
        PqsFilter filter,
        PqsPage page,
        CancellationToken cancellationToken = default)
        where T : ITemplate
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(page);
        return Task.FromResult(Slice(StagedContracts<T>(), page));
    }

    /// <inheritdoc />
    public Task<Contract<T>?> QueryOneAsync<T>(PqsFilter filter, CancellationToken cancellationToken = default)
        where T : ITemplate
    {
        ArgumentNullException.ThrowIfNull(filter);
        var staged = StagedContracts<T>();
        return Task.FromResult(staged.Count > 0 ? staged[0] : null);
    }

    /// <inheritdoc />
    public Task<Contract<T>?> FetchByIdAsync<T>(ContractId<T> contractId, CancellationToken cancellationToken = default)
        where T : ITemplate =>
        Task.FromResult(StagedContracts<T>().FirstOrDefault(c => c.Id.Equals(contractId)));

    /// <inheritdoc />
    public Task<bool> ExistsAsync<T>(ContractId<T> contractId, CancellationToken cancellationToken = default)
        where T : ITemplate =>
        Task.FromResult(StagedContracts<T>().Any(c => c.Id.Equals(contractId)));

    private IReadOnlyList<Contract<T>> StagedContracts<T>()
        where T : ITemplate
    {
        if (_templateResults.TryGetValue(typeof(T), out var staged))
        {
            return (IReadOnlyList<Contract<T>>)staged;
        }

        throw new NotSupportedException(
            $"FakePqsClient has no query results staged for Daml type '{typeof(T).Name}'. Stage some with " +
            $"FakePqsClient.Create().WithQueryResults<{typeof(T).Name}>(...).Build() before exercising this path.");
    }

    private IReadOnlyList<InterfaceContract<TInterface, TView>> StagedInterfaceContracts<TInterface, TView>()
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord
    {
        if (_interfaceResults.TryGetValue(typeof(TInterface), out var staged))
        {
            return (IReadOnlyList<InterfaceContract<TInterface, TView>>)staged;
        }

        throw new NotSupportedException(
            $"FakePqsClient has no interface query results staged for Daml interface '{typeof(TInterface).Name}'. " +
            $"Stage some with FakePqsClient.Create().WithInterfaceQueryResults<{typeof(TInterface).Name}, " +
            $"{typeof(TView).Name}>(...).Build() before exercising this path.");
    }

    private static IReadOnlyList<TItem> Slice<TItem>(IReadOnlyList<TItem> items, PqsPage page) =>
        [.. items.Skip(page.Offset).Take(page.Limit)];
}
