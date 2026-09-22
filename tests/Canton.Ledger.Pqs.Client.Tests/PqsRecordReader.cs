// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;

namespace Canton.Ledger.Pqs.Client.Tests;

internal static class PqsRecordReader
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

    public static DamlLfElementReader OptionalOf(DamlLfElementReader elementReader) =>
        (element, context) => DamlLfJsonDecoders.ReadOptional(element, context, elementReader);
}
