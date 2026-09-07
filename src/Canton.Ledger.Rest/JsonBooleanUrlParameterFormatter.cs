// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Canton.Ledger.Rest.Client.Raw;
using Refit;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Spells a URL parameter the way the JSON Ledger API reads it. Booleans render lowercase, as JSON
/// and OpenAPI spell them, rather than as <see cref="bool.ToString()"/>'s <c>True</c>/<c>False</c>,
/// which Refit's <see cref="DefaultUrlParameterFormatter"/> would otherwise put on the wire for
/// <see cref="IDarApi.UploadDar"/>'s <c>vetAllPackages</c>.
/// </summary>
/// <remarks>
/// Not retired by digital-asset/canton#527: the delta is Refit's
/// <see cref="DefaultUrlParameterFormatter"/> rather than the served document, so it retires when
/// Refit's own default spells a boolean as JSON does.
/// </remarks>
internal sealed class JsonBooleanUrlParameterFormatter : DefaultUrlParameterFormatter
{
    /// <inheritdoc />
    public override string? Format(object? value, ICustomAttributeProvider attributeProvider, Type type) =>
        value is bool flag ? (flag ? "true" : "false") : base.Format(value, attributeProvider, type);
}
