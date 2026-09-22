// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;

namespace Canton.Ledger.Rest.Client.Tests;

/// <summary>
/// Reads a Daml-LF JSON object field by field, the way a generated <c>__ReadDamlLfJson</c> does,
/// for the hand-written test records the REST client decodes.
/// </summary>
internal static class TestRecordReader
{
    public static DamlRecord Read(
        JsonElement json,
        DamlLfJsonDecodeContext context,
        params (string Name, DamlLfElementReader Reader)[] fields)
    {
        var body = DamlLfJsonDecoders.RequireObject(json, context);
        return DamlRecord.Create(fields
            .Select(field => DamlField.Create(
                field.Name,
                field.Reader(DamlLfJsonDecoders.RequireField(body, context, field.Name), context.Field(field.Name))))
            .ToArray());
    }
}
