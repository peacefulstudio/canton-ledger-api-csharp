// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Outcomes;

namespace Canton.Ledger.Testing;

/// <summary>
/// Static factories for the <see cref="ExerciseOutcome{T}"/> variants a command path yields. Each
/// thinly wraps the corresponding public record constructor. Pair these with
/// <see cref="FakeLedgerClientBuilder.WithExerciseResult{TResult}"/>,
/// <see cref="FakeLedgerClientBuilder.WithCreateResult{TTemplate}"/>, or
/// <see cref="FakeLedgerClientBuilder.WithSubmissionOutcome"/>.
/// </summary>
public static class LedgerOutcomes
{
    /// <summary>Builds a successful <see cref="ExerciseOutcome{T}.One"/> outcome.</summary>
    /// <typeparam name="T">The result type carried by the outcome.</typeparam>
    /// <returns>The success outcome.</returns>
    public static ExerciseOutcome<T> One<T>(T result) => new ExerciseOutcome<T>.One(result);

    /// <summary>Builds an empty <see cref="ExerciseOutcome{T}.None"/> outcome.</summary>
    /// <typeparam name="T">The result type the outcome is for.</typeparam>
    /// <returns>The no-result outcome.</returns>
    public static ExerciseOutcome<T> None<T>() => new ExerciseOutcome<T>.None();

    /// <summary>Builds a <see cref="ExerciseOutcome{T}.Many"/> outcome.</summary>
    /// <typeparam name="T">The result type the outcome is for.</typeparam>
    /// <returns>The multiple-result outcome.</returns>
    public static ExerciseOutcome<T> Many<T>(int count, IReadOnlyList<string> contractIds) =>
        new ExerciseOutcome<T>.Many(count, contractIds);

    /// <summary>Builds a structured <see cref="ExerciseOutcome{T}.DamlError"/> outcome.</summary>
    /// <typeparam name="T">The result type the failed outcome is for.</typeparam>
    /// <returns>The Daml-error outcome.</returns>
    public static ExerciseOutcome<T> DamlError<T>(
        DamlErrorCategory category,
        string errorId,
        string message,
        IReadOnlyDictionary<string, string> metadata) =>
        new ExerciseOutcome<T>.DamlError(category, errorId, message, metadata);

    /// <summary>Builds a transport-level <see cref="ExerciseOutcome{T}.InfraError"/> outcome.</summary>
    /// <typeparam name="T">The result type the failed outcome is for.</typeparam>
    /// <returns>The infrastructure-error outcome.</returns>
    public static ExerciseOutcome<T> InfraError<T>(int statusCode, string message, Exception? sourceException = null) =>
        new ExerciseOutcome<T>.InfraError(statusCode, message, sourceException);
}
