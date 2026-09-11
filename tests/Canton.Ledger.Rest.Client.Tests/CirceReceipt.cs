// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;

namespace Canton.Ledger.Rest.Client.Tests;

internal sealed record CirceReceipt : IDamlRecord<CirceReceipt>
{
    public DamlRecord ToRecord() => DamlRecord.Create();

    public static CirceReceipt FromRecord(DamlRecord record) => new();
}
