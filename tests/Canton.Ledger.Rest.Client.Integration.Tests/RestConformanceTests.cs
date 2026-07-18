// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using Canton.Ledger.Rest;
using Xunit;

namespace Canton.Ledger.Rest.Client.Integration.Tests;

[Trait("Category", "Integration")]
public class RestConformanceTests
{
    [Fact]
    public async Task GetLedgerApiVersion_reports_a_version_and_a_features_descriptor()
    {
        await using var lane = await RestConformanceLane.OpenAsync(TestContext.Current.CancellationToken);

        var response = await lane.Api<IVersionServiceApi>()
            .GetLedgerApiVersion(TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrWhiteSpace(response.Version), "GET /v2/version returned an empty version");
        Assert.NotNull(response.Features);

        // TODO(#158): adaptation delta D1 — the JSON Ledger API encodes response keys in camelCase
        // ("userManagement") while the generated POCOs bind proto snake_case names ("user_management"),
        // so multi-word fields land in AdditionalProperties instead of their typed properties.
        Assert.Null(response.Features.UserManagement);
        Assert.True(
            response.Features.AdditionalProperties.ContainsKey("userManagement"),
            "expected the camelCase 'userManagement' feature key to surface in AdditionalProperties");
    }

    [Fact]
    public async Task CheckLiveness_reports_liveness_with_a_success_status()
    {
        await using var lane = await RestConformanceLane.OpenAsync(TestContext.Current.CancellationToken);

        using var response = await lane.Api<IHealthApi>()
            .CheckLiveness(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CheckReadiness_reports_readiness_as_a_status_value()
    {
        await using var lane = await RestConformanceLane.OpenAsync(TestContext.Current.CancellationToken);

        using var response = await lane.Api<IHealthApi>()
            .CheckReadiness(TestContext.Current.CancellationToken);

        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable,
            $"readyz must answer 200 ready or 503 not-ready as a value, got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task GetAuthenticatedUser_identifies_the_validator_user()
    {
        await using var lane = await RestConformanceLane.OpenAsync(TestContext.Current.CancellationToken);

        var response = await lane.Api<IAuthenticatedUserApi>()
            .GetAuthenticatedUser(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(response.User);
        Assert.Equal(lane.Fixture.ValidatorUserId, response.User.Id);

        // TODO(#158): adaptation delta D1 — camelCase wire keys ("primaryParty") do not bind to the
        // generated snake_case properties ("primary_party"); the value is only reachable through
        // AdditionalProperties until the adaptation layer translates the encoding.
        Assert.Null(response.User.PrimaryParty);
        Assert.True(
            response.User.AdditionalProperties.ContainsKey("primaryParty"),
            "expected the camelCase 'primaryParty' key to surface in AdditionalProperties");
    }

    [Fact]
    public async Task ListKnownParties_answers_but_camelCase_response_keys_do_not_bind_to_the_generated_shape()
    {
        await using var lane = await RestConformanceLane.OpenAsync(TestContext.Current.CancellationToken);

        using var wireClient = lane.CreateWireLevelClient();
        using var wireResponse = await wireClient.GetAsync(
            new Uri("/v2/parties", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, wireResponse.StatusCode);
        using var wireBody = JsonDocument.Parse(
            await wireResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var wireParties = wireBody.RootElement.GetProperty("partyDetails");
        Assert.Equal(JsonValueKind.Array, wireParties.ValueKind);
        Assert.NotEqual(0, wireParties.GetArrayLength());

        var response = await lane.Api<IPartyManagementServiceApi>().ListKnownParties(
            page_token: null!,
            page_size: null,
            identity_provider_id: null!,
            filter_party: null!,
            TestContext.Current.CancellationToken);

        // TODO(#158): adaptation delta D1 — the wire's "partyDetails" / "nextPageToken" camelCase keys
        // do not bind to the generated "party_details" / "next_page_token" properties, so the typed
        // party list deserializes to null and the payload sits in AdditionalProperties.
        Assert.Null(response.PartyDetails);
        Assert.True(
            response.AdditionalProperties.ContainsKey("partyDetails"),
            "expected the camelCase 'partyDetails' key to surface in AdditionalProperties");
    }

    [Fact]
    public async Task ListUsers_lists_the_validator_user_but_the_snake_case_paging_parameter_is_ignored()
    {
        await using var lane = await RestConformanceLane.OpenAsync(TestContext.Current.CancellationToken);

        var response = await lane.Api<IUserManagementServiceApi>().ListUsers(
            page_token: null!,
            page_size: 1,
            identity_provider_id: null!,
            TestContext.Current.CancellationToken);

        Assert.NotNull(response.Users);
        Assert.Contains(response.Users, user => user.Id == lane.Fixture.ValidatorUserId);

        // TODO(#158): adaptation delta D3 — the generated [Query] parameters use proto snake_case
        // names ("page_size") but the JSON Ledger API only honors camelCase ("pageSize"), so paging
        // and filtering requested through the generated interfaces are silently ignored by the server.
        Assert.True(
            response.Users.Count > 1,
            $"page_size=1 should cap the page at one user if the server honored it, got {response.Users.Count} "
            + "users — flip this assertion once the adaptation layer sends camelCase query parameters");
    }

    [Fact]
    public async Task GetLedgerEnd_answers_a_numeric_offset_the_generated_shape_cannot_bind()
    {
        await using var lane = await RestConformanceLane.OpenAsync(TestContext.Current.CancellationToken);

        using var wireClient = lane.CreateWireLevelClient();
        using var wireResponse = await wireClient.GetAsync(
            new Uri("/v2/state/ledger-end", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, wireResponse.StatusCode);
        using var wireBody = JsonDocument.Parse(
            await wireResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var wireOffset = wireBody.RootElement.GetProperty("offset");
        Assert.Equal(JsonValueKind.Number, wireOffset.ValueKind);
        Assert.True(wireOffset.GetInt64() >= 0, $"ledger end offset must be non-negative, got {wireOffset}");

        // TODO(#158): adaptation delta D2 — the wire encodes "offset" as a JSON number while the
        // generated GetLedgerEndResponse.Offset is a string (proto3 JSON int64 convention), so the
        // typed call fails to deserialize until the adaptation layer reconciles the numeric encoding.
        await Assert.ThrowsAsync<JsonException>(
            () => lane.Api<IStateServiceApi>().GetLedgerEnd(TestContext.Current.CancellationToken));
    }
}
