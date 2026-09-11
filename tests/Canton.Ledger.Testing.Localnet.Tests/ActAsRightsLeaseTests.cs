// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Testing.Localnet.Tests;

public class ActAsRightsLeaseTests
{
    private const string JsonLedgerApi = "http://localhost:11975";
    private const string UserId = "c87743ab-80e0-4b83-935a-4c0582226691";
    private const string Alice = "alice::ns1";
    private const string Bob = "bob::ns1";

    [Fact]
    public async Task GrantAsync_posts_the_CanActAs_right_to_the_users_rights_endpoint()
    {
        using var participant = new RecordingParticipant();
        await using var lease = NewLease(participant);

        await lease.GrantAsync(Alice, TestContext.Current.CancellationToken);

        var grant = participant.Requests.Should().ContainSingle().Subject;
        grant.Method.Should().Be(HttpMethod.Post);
        grant.Uri.Should().Be(new Uri($"{JsonLedgerApi}/v2/users/{UserId}/rights"));
        grant.BearerToken.Should().Be(RecordingParticipant.AccessToken);
        ActAsPartiesIn(grant.Body).Should().Equal(Alice);
        UserIdIn(grant.Body).Should().Be(UserId);
    }

    [Fact]
    public async Task DisposeAsync_revokes_every_granted_party_in_one_PATCH()
    {
        using var participant = new RecordingParticipant();
        var lease = NewLease(participant);
        await lease.GrantAsync(Alice, TestContext.Current.CancellationToken);
        await lease.GrantAsync(Bob, TestContext.Current.CancellationToken);

        await lease.DisposeAsync();

        var revoke = participant.Requests.Should().HaveCount(3).And.Subject.Last();
        revoke.Method.Should().Be(HttpMethod.Patch);
        revoke.Uri.Should().Be(new Uri($"{JsonLedgerApi}/v2/users/{UserId}/rights"));
        ActAsPartiesIn(revoke.Body).Should().Equal(Alice, Bob);
    }

    [Fact]
    public async Task DisposeAsync_sends_nothing_when_no_right_was_granted()
    {
        using var participant = new RecordingParticipant();
        var lease = NewLease(participant);

        await lease.DisposeAsync();

        participant.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task DisposeAsync_revokes_although_the_cancellation_token_the_grant_ran_on_is_cancelled()
    {
        using var participant = new RecordingParticipant();
        using var cancelledAfterTheGrant = new CancellationTokenSource();
        var lease = NewLease(participant);
        await lease.GrantAsync(Alice, cancelledAfterTheGrant.Token);
        await cancelledAfterTheGrant.CancelAsync();

        await lease.DisposeAsync();

        participant.Requests.Last().Method.Should().Be(HttpMethod.Patch);
    }

    [Fact]
    public async Task DisposeAsync_throws_naming_the_status_and_body_when_the_participant_rejects_the_revoke()
    {
        using var participant = new RecordingParticipant(
            respondTo: request => request.Method == HttpMethod.Patch
                ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("NOT_FOUND") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        var lease = NewLease(participant);
        await lease.GrantAsync(Alice, TestContext.Current.CancellationToken);

        var act = async () => await lease.DisposeAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message
            .Should().Contain("PATCH").And.Contain("400").And.Contain("NOT_FOUND");
    }

    [Fact]
    public async Task GrantAsync_throws_naming_the_status_and_body_when_the_participant_rejects_the_grant()
    {
        using var participant = new RecordingParticipant(
            respondTo: _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("TOO_MANY_USER_RIGHTS"),
            });
        await using var lease = NewLease(participant);

        var act = () => lease.GrantAsync(Alice, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message
            .Should().Contain("POST").And.Contain("400").And.Contain("TOO_MANY_USER_RIGHTS");
    }

    [Fact]
    public async Task DisposeAsync_leaves_nothing_to_revoke_when_the_grant_was_rejected()
    {
        using var participant = new RecordingParticipant(
            respondTo: _ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("no") });
        var lease = NewLease(participant);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => lease.GrantAsync(Alice, TestContext.Current.CancellationToken));

        await lease.DisposeAsync();

        participant.Requests.Should().ContainSingle("only the rejected grant was ever sent");
    }

    [Fact]
    public async Task DisposeAsync_throws_when_the_participant_reports_it_revoked_nothing()
    {
        using var participant = new RecordingParticipant(respondTo: RevokingOnly());
        var lease = NewLease(participant);
        await lease.GrantAsync(Alice, TestContext.Current.CancellationToken);
        await lease.GrantAsync(Bob, TestContext.Current.CancellationToken);

        var act = async () => await lease.DisposeAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message
            .Should().Contain("revoked 0 of 2").And.Contain(Alice).And.Contain(Bob);
    }

    [Fact]
    public async Task DisposeAsync_throws_naming_the_party_the_participant_left_standing()
    {
        using var participant = new RecordingParticipant(respondTo: RevokingOnly(Alice));
        var lease = NewLease(participant);
        await lease.GrantAsync(Alice, TestContext.Current.CancellationToken);
        await lease.GrantAsync(Bob, TestContext.Current.CancellationToken);

        var act = async () => await lease.DisposeAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message
            .Should().Contain("revoked 1 of 2").And.Contain($"still holds {Bob}");
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> RevokingOnly(params string[] parties)
    {
        var revoked = string.Join(",", parties.Select(party =>
            JsonSerializer.Serialize(new { kind = new { CanActAs = new { value = new { party } } } })));
        return request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.Method == HttpMethod.Patch
                ? $$"""{"newlyRevokedRights":[{{revoked}}]}"""
                : "{}"),
        };
    }

    private static ActAsRightsLease NewLease(RecordingParticipant participant) => new(
        new Uri(JsonLedgerApi),
        UserId,
        _ => new ValueTask<string>(RecordingParticipant.AccessToken),
        participant);

    private static string UserIdIn(string body) =>
        JsonDocument.Parse(body).RootElement.GetProperty("userId").GetString()!;

    private static IEnumerable<string> ActAsPartiesIn(string body) =>
        JsonDocument.Parse(body).RootElement.GetProperty("rights").EnumerateArray()
            .Select(right => right.GetProperty("kind").GetProperty("CanActAs")
                .GetProperty("value").GetProperty("party").GetString()!)
            .ToArray();

    private sealed class RecordingParticipant(Func<HttpRequestMessage, HttpResponseMessage>? respondTo = null)
        : HttpMessageHandler
    {
        internal const string AccessToken = "tok-123";

        internal List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.Method, request.RequestUri!, request.Headers.Authorization?.Parameter, body));

            return respondTo?.Invoke(request) ?? Applied(request.Method, body);
        }

        private static HttpResponseMessage Applied(HttpMethod method, string requestBody) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(method == HttpMethod.Patch
                    ? $$"""{"newlyRevokedRights":{{RightsIn(requestBody)}}}"""
                    : "{}"),
            };

        private static string RightsIn(string requestBody) =>
            JsonDocument.Parse(requestBody).RootElement.GetProperty("rights").GetRawText();
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? BearerToken, string Body);
}
