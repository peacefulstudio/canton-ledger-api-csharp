// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Canton.Ledger.Pqs.Client;

/// <summary>
/// Client for querying active Daml contracts from the Participant Query Store (PQS).
/// PQS provides read access to the ledger state via a PostgreSQL database.
/// </summary>
/// <remarks>
/// <para>
/// PQS stores active contracts in a table accessible via the <c>active(@typeId)</c>
/// PostgreSQL function. Each row contains a <c>contract_id</c> column and a <c>payload</c>
/// column with the contract fields as a JSON object (camelCase field names).
/// </para>
/// <para>
/// Queries use the generated Daml C# bindings for type safety. The type identifier
/// is derived from the generated Daml type metadata in the format required by PQS.
/// </para>
/// </remarks>
public interface IPqsClient
{
    /// <summary>
    /// Queries all active contracts of a given template type.
    /// </summary>
    Task<IReadOnlyList<Contract<T>>> QueryAsync<T>(
        CancellationToken cancellationToken = default)
        where T : ITemplate;

    /// <summary>
    /// Queries all active contracts that implement a Daml interface, projecting each row's
    /// participant-computed interface view into <typeparamref name="TView"/>.
    /// </summary>
    /// <remarks>
    /// Routes through the same PQS <c>active(name)</c> function as the template overload, but
    /// passes the interface's package-name-qualified identifier
    /// (<c>{packageName}:{moduleName}:{interfaceName}</c>). PQS returns one row per active contract
    /// implementing the interface, whose <c>payload</c> is the interface view record — not the
    /// implementing template's payload — so results carry the view in
    /// <see cref="InterfaceContract{TInterface, TView}.View"/>. Both type arguments are explicit
    /// (<c>QueryAsync&lt;IHolding, HoldingView&gt;()</c>) because the queried interface and its
    /// deserialized view are distinct types; the <see cref="IHasView{TView}"/> constraint ties them.
    /// </remarks>
    /// <example>
    /// <code>
    /// var holdings = await pqs.QueryAsync&lt;IHolding, HoldingView&gt;(ct);
    /// </code>
    /// </example>
    /// <typeparam name="TInterface">The generated Daml interface marker (e.g. <c>IHolding</c>).</typeparam>
    /// <typeparam name="TView">The interface's view record (e.g. <c>HoldingView</c>).</typeparam>
    Task<IReadOnlyList<InterfaceContract<TInterface, TView>>> QueryAsync<TInterface, TView>(
        CancellationToken cancellationToken = default)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord;

    /// <summary>
    /// Queries active contracts matching a filter.
    /// </summary>
    /// <example>
    /// <code>
    /// var agreements = await pqs.QueryAsync&lt;Agreement&gt;(
    ///     Filter.Or(
    ///         Filter.Field&lt;Agreement&gt;(a => a.Initiator, partyId),
    ///         Filter.Field&lt;Agreement&gt;(a => a.Counterparty, partyId)),
    ///     ct);
    /// </code>
    /// </example>
    Task<IReadOnlyList<Contract<T>>> QueryAsync<T>(
        PqsFilter filter,
        CancellationToken cancellationToken = default)
        where T : ITemplate;

    /// <summary>
    /// Queries a single active contract matching a filter.
    /// Returns the first matching contract, or null if none match.
    /// If multiple contracts match the filter, one is returned non-deterministically (based on database ordering).
    /// </summary>
    Task<Contract<T>?> QueryOneAsync<T>(
        PqsFilter filter,
        CancellationToken cancellationToken = default)
        where T : ITemplate;

    /// <summary>
    /// Fetches a single contract by its contract ID.
    /// </summary>
    Task<Contract<T>?> FetchByIdAsync<T>(
        ContractId<T> contractId,
        CancellationToken cancellationToken = default)
        where T : ITemplate;

    /// <summary>
    /// Checks if a contract exists and is active.
    /// </summary>
    Task<bool> ExistsAsync<T>(
        ContractId<T> contractId,
        CancellationToken cancellationToken = default)
        where T : ITemplate;
}
