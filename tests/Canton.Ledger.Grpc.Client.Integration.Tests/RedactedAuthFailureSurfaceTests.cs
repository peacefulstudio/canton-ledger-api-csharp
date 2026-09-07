// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Microsoft.Extensions.DependencyInjection;
using Peaceful.Canton.Localnet.Testing;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Grpc.Client.Integration.Tests;

/// <summary>
/// A live participant redacts a security-sensitive failure before answering, which strips the
/// structured error the classification would have come from. These rows drive
/// <see cref="ICantonLedgerClient"/> against a running participant with a token it refuses, and read
/// back what a consumer actually holds — the recovery is only worth having if it survives the whole
/// path, and every other row that covers it answers from a fault-injecting transport instead.
/// </summary>
[Trait("Category", "Integration")]
public class RedactedAuthFailureSurfaceTests
{
    private const string SkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_JSON_API_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run this integration test.";

    private const string RefusedToken = "not-a-token-the-participant-issued";
    private const string UnauthenticatedUser = "redacted-auth-surface-user";
    private const string OperationName = "SubmitAndWaitForTransaction";

    private static readonly Party Alice = new("party::alice");

    private static readonly RuntimeCommands.SubmitterInfo Submitter =
        new(new HashSet<Party> { Alice }, new HashSet<Party>());

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_hands_a_consumer_the_auth_category_a_live_participant_redacted()
    {
        if (!EndpointDiscovery.IsLocalnetAvailable())
        {
            Assert.Skip(SkipMessage);
        }

        await using var services = Refused();
        var client = services.GetRequiredService<ICantonLedgerClient>();

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            Submission(), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        var infra = Assert.IsType<ExerciseOutcome<TransactionResult>.InfraError>(outcome);
        Assert.Equal(DamlErrorCategory.AuthInterceptorInvalidAuthenticationCredentials, infra.Category);
    }

    [Fact]
    public async Task OneOrThrowAsync_carries_the_auth_category_a_live_participant_redacted()
    {
        if (!EndpointDiscovery.IsLocalnetAvailable())
        {
            Assert.Skip(SkipMessage);
        }

        await using var services = Refused();
        var client = services.GetRequiredService<ICantonLedgerClient>();

        var thrown = await Assert.ThrowsAsync<LedgerOperationException>(async () =>
            await client
                .TrySubmitAndWaitForTransactionAsync(
                    Submission(), Submitter, cancellationToken: TestContext.Current.CancellationToken)
                .OneOrThrowAsync(OperationName));

        Assert.Equal(DamlErrorCategory.AuthInterceptorInvalidAuthenticationCredentials, thrown.Category);
    }

    private static ServiceProvider Refused() =>
        LocalnetLedgerServices.ForTokenProvider(new StaticTokenProvider(RefusedToken), UnauthenticatedUser);

    private static RuntimeCommands.CommandsSubmission Submission() =>
        RuntimeCommands.CommandsSubmission
            .Single(new RuntimeCommands.CreateCommand(
                new RuntimeIdentifier("test-pkg", "Sample.Foo", "FooBar"),
                new DamlRecord(null, [])))
            .WithCommandId(new RuntimeCommands.CommandId("redacted-auth-surface-cmd"));
}
