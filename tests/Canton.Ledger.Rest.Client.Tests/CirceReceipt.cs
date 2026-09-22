// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Serialization;
using System.Text.Json;
using Daml.Runtime.Data;

namespace Canton.Ledger.Rest.Client.Tests;

internal sealed record CirceReceipt : IDamlRecord<CirceReceipt>
{
    public DamlRecord ToRecord() => DamlRecord.Create();

    public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) => TestRecordReader.Read(json, context);
    public static CirceReceipt FromRecord(DamlRecord record) => new();
}
