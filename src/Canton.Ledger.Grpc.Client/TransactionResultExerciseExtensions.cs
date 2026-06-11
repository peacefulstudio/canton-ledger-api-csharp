// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Canton.Ledger.Grpc.Client;

/// <summary>
/// Typed accessors over <see cref="TransactionResult.ExercisedEvents"/> that deserialize a
/// choice's <see cref="ExercisedEvent.ExerciseResult"/> into a typed return value (e.g.
/// <c>choice GetTrailingTwap : Decimal</c>) through the existing
/// <see cref="DamlValueExtensions.FromDamlValue{TResult}"/> machinery.
/// </summary>
public static class TransactionResultExerciseExtensions
{
    /// <summary>
    /// Returns the typed return value of the single exercised event whose
    /// <see cref="ExercisedEvent.ChoiceName"/> equals <paramref name="choiceName"/> (ordinal),
    /// deserialized via <see cref="DamlValueExtensions.FromDamlValue{TResult}"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="choiceName"/> is <c>null</c> or empty.</exception>
    /// <exception cref="InvalidOperationException">Zero or more than one event matches <paramref name="choiceName"/>.</exception>
    public static TReturn ExerciseResult<TReturn>(this TransactionResult result, string choiceName)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrEmpty(choiceName);

        var matches = MatchingExercisedEvents(result, choiceName);
        return matches.Count switch
        {
            1 => matches[0].ExerciseResult.FromDamlValue<TReturn>()!,
            0 => throw new InvalidOperationException(
                $"Transaction contains no exercised event for choice '{choiceName}'."),
            _ => throw new InvalidOperationException(
                $"Transaction contains {matches.Count} exercised events for choice '{choiceName}', expected exactly 1."),
        };
    }

    /// <summary>
    /// <see cref="ChoiceName"/>-typed overload of
    /// <see cref="ExerciseResult{TReturn}(TransactionResult, string)"/>, matching the typed
    /// submission surface of <c>Daml.Runtime</c> command types.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="choiceName"/> is a default (uninitialized) <see cref="ChoiceName"/>,
    /// or zero or more than one event matches it.
    /// </exception>
    public static TReturn ExerciseResult<TReturn>(this TransactionResult result, ChoiceName choiceName) =>
        result.ExerciseResult<TReturn>(choiceName.Value);

    /// <summary>
    /// Returns the typed return values of every exercised event whose
    /// <see cref="ExercisedEvent.ChoiceName"/> equals <paramref name="choiceName"/> (ordinal),
    /// in transaction order. Returns an empty collection when no event matches.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="choiceName"/> is <c>null</c> or empty.</exception>
    public static IReadOnlyList<TReturn> AllExerciseResults<TReturn>(this TransactionResult result, string choiceName)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrEmpty(choiceName);

        var matches = MatchingExercisedEvents(result, choiceName);
        var results = new TReturn[matches.Count];
        for (var i = 0; i < matches.Count; i++)
        {
            results[i] = matches[i].ExerciseResult.FromDamlValue<TReturn>()!;
        }
        return results;
    }

    /// <summary>
    /// <see cref="ChoiceName"/>-typed overload of
    /// <see cref="AllExerciseResults{TReturn}(TransactionResult, string)"/>, matching the typed
    /// submission surface of <c>Daml.Runtime</c> command types.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="choiceName"/> is a default (uninitialized) <see cref="ChoiceName"/>.
    /// </exception>
    public static IReadOnlyList<TReturn> AllExerciseResults<TReturn>(this TransactionResult result, ChoiceName choiceName) =>
        result.AllExerciseResults<TReturn>(choiceName.Value);

    private static List<ExercisedEvent> MatchingExercisedEvents(TransactionResult result, string choiceName)
    {
        var matches = new List<ExercisedEvent>(result.ExercisedEvents.Count);
        foreach (var exercised in result.ExercisedEvents)
        {
            if (string.Equals(exercised.ChoiceName, choiceName, StringComparison.Ordinal))
            {
                matches.Add(exercised);
            }
        }
        return matches;
    }
}
