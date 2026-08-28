// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions;

namespace Canton.Ledger.Abstractions;

/// <summary>
/// Operation-context accessors for <see cref="LedgerOperationException"/>.
/// The upstream exception type (owned by <c>Daml.Ledger.Abstractions</c>) has no
/// <c>Operation</c> member yet, so the operation name attached by the
/// <see cref="ExerciseOutcomeExtensions"/> throw helpers travels in
/// <see cref="System.Exception.Data"/> and surfaces here as an extension property.
/// </summary>
public static class LedgerOperationExceptionExtensions
{
    internal const string OperationDataKey = "Canton.Ledger.Operation";

    /// <summary>Extension members for <see cref="LedgerOperationException"/>.</summary>
    extension(LedgerOperationException exception)
    {
        /// <summary>
        /// The name of the ledger operation that failed, as supplied to
        /// <see cref="ExerciseOutcomeExtensions.OneOrThrow{T}(Daml.Runtime.Outcomes.ExerciseOutcome{T}, string)"/>
        /// or a sibling helper; <c>null</c> when the exception was raised without
        /// operation context (e.g. by the upstream throwing wrappers).
        /// </summary>
        public string? Operation => exception.Data[OperationDataKey] as string;
    }

    internal static LedgerOperationException WithOperation(
        this LedgerOperationException exception,
        string operationName)
    {
        exception.Data[OperationDataKey] = operationName;
        return exception;
    }
}
