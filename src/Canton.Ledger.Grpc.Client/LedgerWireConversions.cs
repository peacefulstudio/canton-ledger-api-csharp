// Copyright 2026 Peaceful Studio OÜ

using Daml.Runtime.Data;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Grpc.Client;

internal static class LedgerWireConversions
{
    public static RuntimeIdentifier ToRuntimeIdentifier(ProtoIdentifier proto) =>
        new(proto.PackageId, proto.ModuleName, proto.EntityName);

    public static IReadOnlyList<Party> ToPartyList(IEnumerable<string> wireParties)
    {
        var result = new List<Party>();
        foreach (var party in wireParties)
        {
            result.Add((Party)party);
        }
        return result;
    }
}
