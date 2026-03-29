// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using System.Linq.Expressions;

namespace Canton.Ledger.Pqs.Client;

/// <summary>
/// Resolves a member access expression to the corresponding Daml/PQS JSON field name.
/// PQS stores contract payloads with camelCase field names (matching Daml conventions),
/// while generated C# records use PascalCase properties. This class bridges the two.
/// </summary>
internal static class FieldNameResolver
{
    /// <summary>
    /// Extracts the property name from a member expression and converts it to camelCase.
    /// </summary>
    /// <example>
    /// <code>
    /// Resolve&lt;Agreement&gt;(a => a.Initiator)     → "initiator"
    /// Resolve&lt;Agreement&gt;(a => a.SwapsExecuted)  → "swapsExecuted"
    /// Resolve&lt;Agreement&gt;(a => a.BaseAsset)      → "baseAsset"
    /// </code>
    /// </example>
    public static string Resolve<T>(Expression<Func<T, object?>> expression)
    {
        var memberExpression = ExtractMemberExpression(expression.Body);
        var propertyName = memberExpression.Member.Name;
        return ToCamelCase(propertyName);
    }

    private static MemberExpression ExtractMemberExpression(Expression expression) =>
        expression switch
        {
            MemberExpression { Expression: ParameterExpression } member => member,
            // Handle boxing: value types get wrapped in Convert(member)
            UnaryExpression { NodeType: ExpressionType.Convert, Operand: MemberExpression { Expression: ParameterExpression } member } => member,
            MemberExpression member => throw new ArgumentException(
                $"Nested property access ('{member}') is not supported. " +
                $"Only direct property access (e.g., x => x.PropertyName) is allowed.",
                nameof(expression)),
            _ => throw new ArgumentException(
                $"Expression must be a simple property access (e.g., x => x.PropertyName), " +
                $"but got {expression.NodeType}: {expression}",
                nameof(expression))
        };

    /// <summary>
    /// Converts a PascalCase property name to camelCase.
    /// Handles leading uppercase runs correctly: "PQSClient" → "pqsClient".
    /// </summary>
    internal static string ToCamelCase(string pascalCase)
    {
        if (string.IsNullOrEmpty(pascalCase))
            return pascalCase;

        if (!char.IsUpper(pascalCase[0]))
            return pascalCase;

        var chars = pascalCase.ToCharArray();

        // Lowercase leading uppercase run (e.g., "PQSClient" → "pqsClient")
        for (var i = 0; i < chars.Length; i++)
        {
            if (!char.IsUpper(chars[i]))
                break;

            // If this is the last uppercase char in a run of >1, keep it uppercase
            // (e.g., "PQSClient" → lowercase P, Q, but keep S because next char is lowercase)
            if (i > 0 && i + 1 < chars.Length && char.IsLower(chars[i + 1]))
                break;

            chars[i] = char.ToLowerInvariant(chars[i]);
        }

        return new string(chars);
    }
}
