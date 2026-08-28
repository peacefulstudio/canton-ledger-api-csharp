// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;

namespace Canton.Ledger.Abstractions;

/// <summary>
/// Represents a filter condition for PQS queries.
/// Filters are built via the <see cref="Filter"/> static class and generate
/// parameterized SQL WHERE clauses. Field names are derived from strongly-typed
/// expressions — never from user input — eliminating SQL injection by construction.
/// </summary>
public abstract partial record PqsFilter
{
    internal abstract string ToSqlClause(ICollection<(string Name, string Value)> parameters, ref int paramIndex);

    internal sealed record FieldEquals(string FieldName, string Value) : PqsFilter
    {
        internal override string ToSqlClause(ICollection<(string Name, string Value)> parameters, ref int paramIndex)
        {
            if (!SafeFieldNamePattern().IsMatch(FieldName))
                throw new ArgumentException($"Invalid field name: '{FieldName}'");

            var paramName = $"@p{paramIndex++}";
            parameters.Add((paramName, Value));
            return $"payload->>'{FieldName}' = {paramName}";
        }
    }

    internal sealed record OrFilter(PqsFilter[] Filters) : PqsFilter
    {
        internal override string ToSqlClause(ICollection<(string Name, string Value)> parameters, ref int paramIndex)
        {
            var parts = new string[Filters.Length];
            for (var i = 0; i < Filters.Length; i++)
                parts[i] = Filters[i].ToSqlClause(parameters, ref paramIndex);
            return $"({string.Join(" OR ", parts)})";
        }
    }

    internal sealed record AndFilter(PqsFilter[] Filters) : PqsFilter
    {
        internal override string ToSqlClause(ICollection<(string Name, string Value)> parameters, ref int paramIndex)
        {
            var parts = new string[Filters.Length];
            for (var i = 0; i < Filters.Length; i++)
                parts[i] = Filters[i].ToSqlClause(parameters, ref paramIndex);
            return $"({string.Join(" AND ", parts)})";
        }
    }

    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*$")]
    private static partial Regex SafeFieldNamePattern();
}
