// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Grpc.Client;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Data;
using Peaceful.Canton.Localnet.Testing;
using Richtypes;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Grpc.Client.Integration.Tests;

[Trait("Category", "Integration")]
public class CompletionStreamRoundTripTests
{
    private const string GrpcUrlEnv = "CANTON_LOCALNET_A_VALIDATOR_1_GRPC_URL";
    private const string DefaultGrpcUrl = "http://localhost:11901";

    private const string SkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_JSON_API_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run this integration test.";

    private static string DarPath() => Path.Combine(
        AppContext.BaseDirectory, "testdata", "richtypes", "richtypes.dar");

    private static LedgerClient NewClient(LocalnetFixture fixture, string userId)
    {
        var grpcAddress = Environment.GetEnvironmentVariable(GrpcUrlEnv) ?? DefaultGrpcUrl;
        var tokenProvider = new LocalnetTokenProvider(fixture.TokenProvider.GetAccessTokenAsync);
        return new LedgerClient(
            new LedgerClientOptions { GrpcAddress = grpcAddress, UserId = userId },
            tokenProvider);
    }

    [Fact]
    public async Task Submit_completion_is_observed_on_CompletionStreamAsync_from_pre_submit_offset()
    {
        if (!EndpointDiscovery.IsLocalnetAvailable())
        {
            Assert.Skip(SkipMessage);
        }

        await using var fixture = LocalnetFixture.FromEnvironment();

        var darOutcome = await fixture.UploadDarAsync(DarPath(), TestContext.Current.CancellationToken);
        Assert.True(
            darOutcome is DarUploadOutcome.Uploaded or DarUploadOutcome.AlreadyKnown,
            $"Unexpected DAR upload outcome: {darOutcome}");

        var party = await fixture.AllocatePartyAsync("cdg", cancellationToken: TestContext.Current.CancellationToken);
        var owner = new Party(party.PartyId);
        var userId = fixture.ValidatorUserId;
        await fixture.GrantUserRightsAsync(
            userId,
            actAs: new[] { party.PartyId },
            cancellationToken: TestContext.Current.CancellationToken);

        using var client = NewClient(fixture, userId);

        var preSubmitOffset = (await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken)).Value;

        var commandId = Guid.NewGuid().ToString();
        var submission = RuntimeCommands.CommandsSubmission
            .Single(RuntimeCommands.CreateCommand.For(new Marker(owner)))
            .WithActAs(owner)
            .WithCommandId(new RuntimeCommands.CommandId(commandId));

        var returnedCommandId = await client.SubmitAsync(submission, TestContext.Current.CancellationToken);
        Assert.Equal(commandId, returnedCommandId.Value);

        var completion = await ObserveCompletionAsync(client, owner, preSubmitOffset, commandId);

        Assert.NotNull(completion);
        Assert.Equal(commandId, completion!.CommandId);
        Assert.Equal(0, completion.Status?.Code ?? 0);
        Assert.False(string.IsNullOrWhiteSpace(completion.UpdateId), "successful completion carries an update id");
    }

    private static async Task<Completion?> ObserveCompletionAsync(
        LedgerClient client, Party owner, long beginExclusiveOffset, string commandId)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await foreach (var streamEvent in client.CompletionStreamAsync(owner, beginExclusiveOffset, cts.Token))
        {
            if (streamEvent is CompletionStreamEvent.CommandCompleted { Completion: var completion }
                && completion.CommandId == commandId)
            {
                return completion;
            }
        }
        return null;
    }
}
