// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions;
using Daml.Runtime.Outcomes;

namespace Canton.Ledger.Abstractions;

/// <summary>
/// Contextual unwrap helpers for <see cref="ExerciseOutcome{T}"/>: turn a non-success
/// outcome into a <see cref="LedgerOperationException"/> whose message and
/// <see cref="LedgerOperationExceptionExtensions">Operation property</see> name the
/// operation that failed. Complements the upstream throwing wrappers in
/// <c>Daml.Ledger.Abstractions.Extensions.ThrowingExercise</c>, which carry no
/// operation context.
/// </summary>
public static class ExerciseOutcomeExtensions
{
    /// <summary>
    /// Returns the single success result, or throws a <see cref="LedgerOperationException"/>
    /// that names <paramref name="operationName"/> in its message and exposes it via the
    /// <c>Operation</c> extension property. A <see cref="ExerciseOutcome{T}.DamlError"/>
    /// outcome surfaces its category, error id, and metadata on the exception; an
    /// <see cref="ExerciseOutcome{T}.InfraError"/> outcome surfaces its transport status
    /// code and source exception.
    /// </summary>
    /// <param name="outcome">The outcome to unwrap.</param>
    /// <param name="operationName">
    /// Human-readable name of the operation the outcome came from (e.g. <c>"Mint"</c>,
    /// <c>"Agreement.Accept"</c>); used verbatim in the exception message.
    /// </param>
    public static T OneOrThrow<T>(this ExerciseOutcome<T> outcome, string operationName)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);

        return outcome switch
        {
            ExerciseOutcome<T>.One one => one.Result,
            ExerciseOutcome<T>.None => throw new LedgerOperationException(
                    $"{operationName}: expected exactly one result but the operation produced none (None).")
                .WithOperation(operationName),
            ExerciseOutcome<T>.Many many => throw new LedgerOperationException(
                    $"{operationName}: expected exactly one result but the operation produced {many.Count} " +
                    $"(contract ids: {string.Join(", ", many.ContractIds)}).")
                .WithOperation(operationName),
            ExerciseOutcome<T>.DamlError damlError => throw damlError.ToException(operationName),
            ExerciseOutcome<T>.InfraError infraError => throw infraError.ToException(operationName),
            _ => throw new LedgerOperationException(
                    $"{operationName}: unexpected outcome {outcome.GetType().Name}.")
                .WithOperation(operationName),
        };
    }

    /// <summary>
    /// Awaits the outcome and unwraps it with
    /// <see cref="OneOrThrow{T}(ExerciseOutcome{T}, string)"/>, so a <c>Try*</c> call can
    /// be unwrapped fluently:
    /// <c>await writer.TryExerciseAsync&lt;T&gt;(...).OneOrThrowAsync("Mint")</c>.
    /// </summary>
    /// <param name="outcomeTask">The pending outcome to await and unwrap.</param>
    /// <param name="operationName">
    /// Human-readable name of the operation the outcome came from; used verbatim in the
    /// exception message.
    /// </param>
    public static async Task<T> OneOrThrowAsync<T>(this Task<ExerciseOutcome<T>> outcomeTask, string operationName)
    {
        ArgumentNullException.ThrowIfNull(outcomeTask);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);

        var outcome = await outcomeTask.ConfigureAwait(false);
        return outcome.OneOrThrow(operationName);
    }

    /// <summary>
    /// Throws a contextual <see cref="LedgerOperationException"/> for a
    /// <see cref="ExerciseOutcome{T}.DamlError"/> or
    /// <see cref="ExerciseOutcome{T}.InfraError"/> outcome and returns silently otherwise —
    /// for callers that discard the result and treat <c>One</c>, <c>None</c>, and
    /// <c>Many</c> alike as success.
    /// </summary>
    /// <param name="outcome">The outcome to check.</param>
    /// <param name="operationName">
    /// Human-readable name of the operation the outcome came from; used verbatim in the
    /// exception message.
    /// </param>
    public static void ThrowIfError<T>(this ExerciseOutcome<T> outcome, string operationName)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);

        switch (outcome)
        {
            case ExerciseOutcome<T>.DamlError damlError:
                throw damlError.ToException(operationName);
            case ExerciseOutcome<T>.InfraError infraError:
                throw infraError.ToException(operationName);
        }
    }

    private static LedgerOperationException ToException<T>(
        this ExerciseOutcome<T>.DamlError error,
        string operationName) =>
        new LedgerOperationException(
                $"{operationName}: Daml error [{error.Category}/{error.ErrorId}]: {error.Message}",
                error.Category,
                error.ErrorId,
                error.Metadata)
            .WithOperation(operationName);

    private static LedgerOperationException ToException<T>(
        this ExerciseOutcome<T>.InfraError error,
        string operationName) =>
        new LedgerOperationException(
                $"{operationName}: infrastructure error [{error.StatusCode}]: {error.Message}",
                error.StatusCode,
                error.SourceException)
            .WithOperation(operationName);
}
