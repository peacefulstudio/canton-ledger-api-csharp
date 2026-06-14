// Copyright 2026 Peaceful Studio OÜ

using System.Linq.Expressions;
using Daml.Runtime.Contracts;

namespace Canton.Ledger.Pqs.Client;

/// <summary>
/// Builds type-safe PQS query filters using expressions.
/// Field names are resolved from strongly-typed expressions against the generated Daml C# bindings,
/// ensuring they always match the PQS JSON payload structure.
/// </summary>
/// <example>
/// <code>
/// // Single field equality
/// Filter.Field&lt;Agreement&gt;(a => a.Initiator, partyId)
///
/// // OR condition
/// Filter.Or(
///     Filter.Field&lt;Agreement&gt;(a => a.Initiator, partyId),
///     Filter.Field&lt;Agreement&gt;(a => a.Counterparty, partyId))
///
/// // AND condition
/// Filter.And(
///     Filter.Field&lt;Agreement&gt;(a => a.Status, "Active"),
///     Filter.Field&lt;Agreement&gt;(a => a.Initiator, partyId))
/// </code>
/// </example>
public static class Filter
{
    /// <summary>
    /// Creates a filter that matches contracts where the specified field equals the given value.
    /// The field name is derived from the expression and converted to camelCase to match
    /// the PQS JSON payload format.
    /// </summary>
    /// <typeparam name="T">The Daml template type.</typeparam>
    /// <param name="selector">A property selector expression (e.g., <c>a => a.Initiator</c>).</param>
    /// <param name="value">The value to match.</param>
    /// <returns>A filter matching contracts where the specified field equals the given value.</returns>
    public static PqsFilter Field<T>(Expression<Func<T, object?>> selector, string value)
        where T : ITemplate
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(value);
        var fieldName = FieldNameResolver.Resolve<T>(selector);
        return new PqsFilter.FieldEquals(fieldName, value);
    }

    /// <summary>
    /// Combines filters with OR logic.
    /// </summary>
    public static PqsFilter Or(params PqsFilter[] filters)
    {
        ArgumentNullException.ThrowIfNull(filters);
        if (filters.Length == 0)
            throw new ArgumentException("At least one filter is required.", nameof(filters));
        for (var i = 0; i < filters.Length; i++)
            ArgumentNullException.ThrowIfNull(filters[i], $"filters[{i}]");
        return filters.Length == 1 ? filters[0] : new PqsFilter.OrFilter(filters);
    }

    /// <summary>
    /// Combines filters with AND logic.
    /// </summary>
    public static PqsFilter And(params PqsFilter[] filters)
    {
        ArgumentNullException.ThrowIfNull(filters);
        if (filters.Length == 0)
            throw new ArgumentException("At least one filter is required.", nameof(filters));
        for (var i = 0; i < filters.Length; i++)
            ArgumentNullException.ThrowIfNull(filters[i], $"filters[{i}]");
        return filters.Length == 1 ? filters[0] : new PqsFilter.AndFilter(filters);
    }
}
