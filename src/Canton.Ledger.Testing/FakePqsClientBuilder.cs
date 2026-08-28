// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Canton.Ledger.Testing;

/// <summary>
/// Fluent builder that stages the query results a <see cref="FakePqsClient"/> replays, keyed by
/// Daml type. Obtain one from <see cref="FakePqsClient.Create"/>, chain <c>With*</c> calls, then
/// call <see cref="Build"/>.
/// </summary>
public sealed class FakePqsClientBuilder
{
    private readonly Dictionary<Type, object> _templateResults = [];
    private readonly Dictionary<Type, object> _interfaceResults = [];

    /// <summary>
    /// Stages the active contracts of template type <typeparamref name="T"/> that
    /// <see cref="FakePqsClient.QueryAsync{T}(CancellationToken)"/>,
    /// <see cref="FakePqsClient.QueryAsync{T}(PqsFilter, CancellationToken)"/>,
    /// <see cref="FakePqsClient.QueryOneAsync{T}"/>, <see cref="FakePqsClient.FetchByIdAsync{T}"/>,
    /// and <see cref="FakePqsClient.ExistsAsync{T}"/> read from. Call with no arguments to
    /// explicitly stage "no active contracts of this type".
    /// </summary>
    /// <typeparam name="T">The Daml template type the contracts are for.</typeparam>
    /// <returns>The same builder, for chaining.</returns>
    public FakePqsClientBuilder WithQueryResults<T>(params Contract<T>[] contracts)
        where T : ITemplate
    {
        ArgumentNullException.ThrowIfNull(contracts);
        _templateResults[typeof(T)] = contracts.ToArray();
        return this;
    }

    /// <summary>
    /// Stages the active contracts observed through interface <typeparamref name="TInterface"/>
    /// that <see cref="FakePqsClient.QueryAsync{TInterface, TView}(CancellationToken)"/> and its
    /// paged overload replay.
    /// </summary>
    /// <typeparam name="TInterface">The Daml interface marker the contracts are queried through.</typeparam>
    /// <typeparam name="TView">The interface's view record type.</typeparam>
    /// <returns>The same builder, for chaining.</returns>
    public FakePqsClientBuilder WithInterfaceQueryResults<TInterface, TView>(
        params InterfaceContract<TInterface, TView>[] contracts)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord
    {
        ArgumentNullException.ThrowIfNull(contracts);
        _interfaceResults[typeof(TInterface)] = contracts.ToArray();
        return this;
    }

    /// <summary>Builds a <see cref="FakePqsClient"/> from the currently staged query results.</summary>
    /// <returns>
    /// A fake whose behaviour is a snapshot of this builder; later mutation of the builder does
    /// not affect an already-built client.
    /// </returns>
    public FakePqsClient Build() => new(
        new Dictionary<Type, object>(_templateResults),
        new Dictionary<Type, object>(_interfaceResults));
}
