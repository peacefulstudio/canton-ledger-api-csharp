// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Rest.Client.Raw;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Tests;

public class RestRefitSettingsTests
{
    [Fact]
    public async Task Create_serializes_bodies_with_generated_wire_names_and_omits_unset_optional_fields()
    {
        var (api, transport) = RestApiFactory.Build<IPartyManagementServiceApi>();
        transport.WithResponse(HttpStatusCode.OK, """{"partyDetails":{"party":"alice::ns1"}}""");

        await api.AllocateParty(
            new AllocatePartyRequest { PartyIdHint = "alice" },
            TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        body.RootElement.GetProperty("partyIdHint").GetString().Should().Be("alice");
        body.RootElement.TryGetProperty("synchronizerId", out _).Should()
            .BeFalse("unset optional fields must be omitted on the wire as proto3 JSON does");
        body.RootElement.TryGetProperty("localMetadata", out _).Should().BeFalse();
    }
}
