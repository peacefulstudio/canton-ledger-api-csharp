// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Linq.Expressions;
using System.Reflection;
using Daml.Runtime.Data;

namespace Canton.Ledger.Abstractions;

internal static class DamlFieldName
{
    public static string Resolve<T>(Expression<Func<T, object?>> expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var member = ExtractMemberExpression(expression.Body).Member;
        var attribute = member.GetCustomAttribute<DamlFieldAttribute>()
            ?? throw new InvalidOperationException(
                $"Property '{member.DeclaringType?.Name ?? typeof(T).Name}.{member.Name}' carries no [DamlField] metadata, so its " +
                $"PQS wire field name cannot be resolved. Regenerate the Daml bindings with a codegen " +
                $"that emits field-name metadata; the typed filter DSL reads the wire name from that " +
                $"attribute and never guesses from the C# property name.");
        return attribute.Name;
    }

    private static MemberExpression ExtractMemberExpression(Expression expression) =>
        expression switch
        {
            MemberExpression { Expression: ParameterExpression } member => member,
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
}
