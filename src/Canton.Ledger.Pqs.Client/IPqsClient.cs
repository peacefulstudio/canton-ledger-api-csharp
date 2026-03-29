// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Daml.Codegen.CSharp.Runtime.Contracts;

namespace Canton.Ledger.Pqs.Client;

/// <summary>
/// Client for querying active Daml contracts from the Participant Query Store (PQS).
/// PQS provides read access to the ledger state via a PostgreSQL database.
/// </summary>
/// <remarks>
/// <para>
/// PQS stores active contracts in a table accessible via the <c>active(@templateId)</c>
/// PostgreSQL function. Each row contains a <c>contract_id</c> column and a <c>payload</c>
/// column with the contract fields as a JSON object (camelCase field names).
/// </para>
/// <para>
/// Queries use the generated Daml C# bindings for type safety. The template identifier
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
