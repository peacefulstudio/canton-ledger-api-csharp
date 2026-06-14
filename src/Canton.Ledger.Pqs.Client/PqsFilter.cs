// Copyright 2026 Peaceful Studio OÜ

using System.Text.RegularExpressions;
using Npgsql;

namespace Canton.Ledger.Pqs.Client;

/// <summary>
/// Represents a filter condition for PQS queries.
/// Filters are built via the <see cref="Filter"/> static class and generate
/// parameterized SQL WHERE clauses. Field names are derived from strongly-typed
/// expressions — never from user input — eliminating SQL injection by construction.
/// </summary>
public abstract partial record PqsFilter
{
    /// <summary>
    /// Generates a parameterized SQL WHERE clause fragment.
    /// </summary>
    /// <param name="cmd">The NpgsqlCommand to add parameters to.</param>
    /// <param name="paramIndex">Counter for generating unique parameter names (@p0, @p1, ...).</param>
    /// <returns>A SQL fragment like <c>payload->>'fieldName' = @p0</c>.</returns>
    internal abstract string ToSqlClause(NpgsqlCommand cmd, ref int paramIndex);

    internal sealed record FieldEquals(string FieldName, string Value) : PqsFilter
    {
        internal override string ToSqlClause(NpgsqlCommand cmd, ref int paramIndex)
        {
            if (!SafeFieldNamePattern().IsMatch(FieldName))
                throw new ArgumentException($"Invalid field name: '{FieldName}'");

            var paramName = $"@p{paramIndex++}";
            cmd.Parameters.AddWithValue(paramName, Value);
            return $"payload->>'{FieldName}' = {paramName}";
        }
    }

    internal sealed record OrFilter(PqsFilter[] Filters) : PqsFilter
    {
        internal override string ToSqlClause(NpgsqlCommand cmd, ref int paramIndex)
        {
            var parts = new string[Filters.Length];
            for (var i = 0; i < Filters.Length; i++)
                parts[i] = Filters[i].ToSqlClause(cmd, ref paramIndex);
            return $"({string.Join(" OR ", parts)})";
        }
    }

    internal sealed record AndFilter(PqsFilter[] Filters) : PqsFilter
    {
        internal override string ToSqlClause(NpgsqlCommand cmd, ref int paramIndex)
        {
            var parts = new string[Filters.Length];
            for (var i = 0; i < Filters.Length; i++)
                parts[i] = Filters[i].ToSqlClause(cmd, ref paramIndex);
            return $"({string.Join(" AND ", parts)})";
        }
    }

    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*$")]
    private static partial Regex SafeFieldNamePattern();
}
