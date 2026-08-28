// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections;
using System.Reflection;
using Refit;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Serializes URL parameters the way the JSON Ledger API declares them. Collection-valued path
/// parameters use the OpenAPI <c>simple</c> style (comma-delimited), which the spec declares for
/// array path parameters such as <c>GET /v2/parties/{parties}</c>; Refit's
/// <see cref="DefaultUrlParameterFormatter"/> would otherwise <c>ToString()</c> the collection
/// itself into the path segment. Query collections are unaffected: Refit expands those per element
/// before formatting. Booleans render lowercase, as JSON and OpenAPI spell them, rather than as
/// <see cref="bool.ToString()"/>'s <c>True</c>/<c>False</c>.
/// </summary>
internal sealed class SimpleStylePathParameterFormatter : DefaultUrlParameterFormatter
{
    /// <inheritdoc />
    public override string? Format(object? value, ICustomAttributeProvider attributeProvider, Type type)
    {
        if (value is bool flag)
            return flag ? "true" : "false";

        if (value is IEnumerable elements and not string)
            return string.Join(",", elements.Cast<object?>().Select(e => base.Format(e, attributeProvider, type)));

        return base.Format(value, attributeProvider, type);
    }
}
