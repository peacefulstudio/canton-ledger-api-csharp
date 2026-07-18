// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public class RestRefitSettingsTests
{
    [Fact]
    public async Task Create_serializes_bodies_with_generated_wire_names_and_omits_unset_optional_fields()
    {
        var (api, transport) = RestApiFactory.Build<IPartyManagementServiceApi>();
        transport.WithResponse(HttpStatusCode.OK, """{"party_details":{"party":"alice::ns1"}}""");

        await api.AllocateParty(
            new AllocatePartyRequest { PartyIdHint = "alice" },
            TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        body.RootElement.GetProperty("party_id_hint").GetString().Should().Be("alice");
        body.RootElement.TryGetProperty("synchronizer_id", out _).Should()
            .BeFalse("unset optional fields must be omitted on the wire as proto3 JSON does");
        body.RootElement.TryGetProperty("local_metadata", out _).Should().BeFalse();
    }
}
