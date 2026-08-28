// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Linq;
using System.Net;
using System.Text.Json;
using Canton.Ledger.Rest.Client.Raw;
using Xunit;

#pragma warning disable CANTONREST001

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
        Assert.NotNull(response.Features.UserManagement);
        Assert.False(
            response.Features.AdditionalProperties.ContainsKey("userManagement"),
            "the camelCase 'userManagement' feature key must bind to the typed property, not AdditionalProperties");
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
        Assert.NotNull(response.User.PrimaryParty);
        Assert.False(
            response.User.AdditionalProperties.ContainsKey("primaryParty"),
            "the camelCase 'primaryParty' key must bind to the typed property, not AdditionalProperties");
    }

    [Fact]
    public async Task ListKnownParties_binds_the_camelCase_partyDetails_and_nextPageToken_into_the_generated_shape()
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
        var wirePartyIds = wireParties.EnumerateArray()
            .Select(party => party.GetProperty("party").GetString())
            .ToList();

        var response = await lane.Api<IPartyManagementServiceApi>().ListKnownParties(
            pageToken: null!,
            pageSize: null,
            identityProviderId: null!,
            filterParty: null!,
            TestContext.Current.CancellationToken);

        Assert.NotNull(response.PartyDetails);
        Assert.Equal(wirePartyIds, response.PartyDetails.Select(party => party.Party));
    }

    [Fact]
    public async Task ListUsers_honors_the_page_size_paging_parameter()
    {
        await using var lane = await RestConformanceLane.OpenAsync(TestContext.Current.CancellationToken);

        var response = await lane.Api<IUserManagementServiceApi>().ListUsers(
            pageToken: null!,
            pageSize: 1,
            identityProviderId: null!,
            TestContext.Current.CancellationToken);

        Assert.NotNull(response.Users);
        Assert.Single(response.Users);
    }

    [Fact]
    public async Task GetLedgerEndAsync_binds_the_numeric_offset_into_a_ledger_offset()
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

        var wireEndBeforeClientRead = wireOffset.GetInt64();

        var end = await lane.LedgerClient.GetLedgerEndAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(
            end.Value >= wireEndBeforeClientRead,
            $"ledger end must never go backwards, but the later client read {end.Value} "
            + $"is below the earlier wire read {wireEndBeforeClientRead}");
        Assert.True(end.Value >= 0, $"ledger end offset must be non-negative, got {end.Value}");
    }
}
