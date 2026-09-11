// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Kernel.Wire;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Google.Protobuf;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Grpc.Client;

internal static class LedgerWireConversions
{
    public static RuntimeIdentifier ToRuntimeIdentifier(ProtoIdentifier proto) =>
        new(proto.PackageId, proto.ModuleName, proto.EntityName);

    internal static string? ToKeyHash(ByteString contractKeyHash) =>
        contractKeyHash.IsEmpty ? null : Convert.ToBase64String(contractKeyHash.Span);

    internal static ContractKey? ContractKeyOf(ProtoCreatedEvent created, RuntimeIdentifier runtimeTemplateId)
    {
        if (created.ContractKey is null)
        {
            return null;
        }

        return new ContractKey(GrpcValueDecoder.ToDamlValue(created.ContractKey), runtimeTemplateId)
        {
            KeyHash = ToKeyHash(created.ContractKeyHash),
        };
    }

    public static RuntimeCommands.CommandId? ToCommandId(string commandId) =>
        commandId.Length == 0 ? null : MalformedResponse.Decoding(commandId, ToNamedCommandId);

    private static RuntimeCommands.CommandId ToNamedCommandId(string commandId) =>
        (RuntimeCommands.CommandId)commandId;

    public static LedgerOffset ToLedgerOffset(long wireOffset) =>
        MalformedResponse.Decoding(wireOffset, LedgerOffset.At);

    public static EquatableArray<Party> ToPartyList(IEnumerable<string> wireParties) =>
        MalformedResponse.Decoding(wireParties, ToParties);

    private static EquatableArray<Party> ToParties(IEnumerable<string> wireParties)
    {
        var result = new List<Party>();
        foreach (var party in wireParties)
        {
            result.Add((Party)party);
        }
        return EquatableArray.Create(result);
    }
}
