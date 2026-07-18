// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections;
using System.Reflection;
using Refit;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Serializes collection-valued path parameters in the OpenAPI <c>simple</c> style
/// (comma-delimited), which the spec declares for array path parameters such as
/// <c>GET /v2/parties/{parties}</c>. Refit's <see cref="DefaultUrlParameterFormatter"/>
/// would otherwise <c>ToString()</c> the collection itself into the path segment.
/// Query collections are unaffected: Refit expands those per element before formatting.
/// </summary>
internal sealed class SimpleStylePathParameterFormatter : DefaultUrlParameterFormatter
{
    /// <inheritdoc />
    public override string? Format(object? value, ICustomAttributeProvider attributeProvider, Type type)
    {
        if (value is IEnumerable elements and not string)
            return string.Join(",", elements.Cast<object?>().Select(e => base.Format(e, attributeProvider, type)));

        return base.Format(value, attributeProvider, type);
    }
}
