// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Kernel.Wire;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Data;
using Daml.Runtime.Grpc;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;

namespace Canton.Ledger.Grpc.Client;

internal static class GrpcValueDecoder
{
    public static DamlValue ToDamlValue(Value value) =>
        MalformedResponse.Decoding(value, DamlValueConverter.FromProtoValue);

    public static DamlRecord ToDamlRecord(Record record) =>
        MalformedResponse.Decoding(record, DamlValueConverter.FromProtoRecord);

    public static string ToPayload(Record record) =>
        MalformedResponse.Decoding(record, wire => wire.ToString());

    public static DateTimeOffset? ToCreatedAt(ProtoCreatedEvent created) =>
        created.CreatedAt is null
            ? null
            : MalformedResponse.Decoding(created.CreatedAt, timestamp => timestamp.ToDateTimeOffset());
}
