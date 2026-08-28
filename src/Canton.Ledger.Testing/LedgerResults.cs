// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;

namespace Canton.Ledger.Testing;

/// <summary>
/// Static factories for the transaction/contract result records a command path yields
/// (<see cref="TransactionResult"/>, <see cref="SubmitAndWaitResult"/>, <see cref="Contract{T}"/>).
/// Each thinly wraps the corresponding public record constructor.
/// </summary>
public static class LedgerResults
{
    /// <summary>Builds a <see cref="TransactionResult"/>.</summary>
    /// <returns>The transaction result.</returns>
    public static TransactionResult Transaction(
        string updateId,
        LedgerOffset completionOffset,
        IReadOnlyList<CreatedContract> createdContracts,
        IReadOnlyList<string> archivedContractIds,
        CommandId commandId) =>
        new(updateId, completionOffset, createdContracts, archivedContractIds, commandId);

    /// <summary>Builds a <see cref="SubmitAndWaitResult"/>.</summary>
    /// <returns>The submit-and-wait result.</returns>
    public static SubmitAndWaitResult SubmitAndWait(
        CommandId commandId,
        string updateId,
        LedgerOffset completionOffset) =>
        new(commandId, updateId, completionOffset);

    /// <summary>Builds a typed <see cref="Contract{T}"/>.</summary>
    /// <typeparam name="T">The template type of the contract.</typeparam>
    /// <returns>The typed contract.</returns>
    public static Contract<T> TypedContract<T>(ContractId<T> id, T data)
        where T : ITemplate =>
        new(id, data);
}
