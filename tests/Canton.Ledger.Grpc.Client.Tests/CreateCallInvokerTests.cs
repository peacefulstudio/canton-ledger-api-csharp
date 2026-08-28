// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Kernel.Authentication;
using Com.Daml.Ledger.Api.V2;
using Com.Daml.Ledger.Api.V2.Admin;
using AwesomeAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public class CreateCallInvokerTests
{
    private static readonly LedgerClientOptions Options = new()
    {
        GrpcAddress = "https://localhost:5001",
    };

    private static LedgerClient CreateLedgerClient(GrpcChannel channel)
    {
        var callInvoker = Substitute.For<CallInvoker>();
        return new LedgerClient(
            Options,
            channel,
            Substitute.ForPartsOf<CommandService.CommandServiceClient>(callInvoker),
            new StaticTokenProvider("test-token"));
    }

    private static AdminClient CreateAdminClient(GrpcChannel channel)
    {
        var callInvoker = Substitute.For<CallInvoker>();
        return new AdminClient(
            Options,
            channel,
            Substitute.ForPartsOf<PartyManagementService.PartyManagementServiceClient>(callInvoker),
            Substitute.ForPartsOf<UserManagementService.UserManagementServiceClient>(callInvoker),
            new StaticTokenProvider("test-token"));
    }

    [Fact]
    public void CreateCallInvoker_returns_the_authenticated_invoker_on_LedgerClient()
    {
        using var channel = GrpcChannel.ForAddress(Options.GrpcAddress);
        using var client = CreateLedgerClient(channel);

        client.CreateCallInvoker().Should().BeOfType<AuthenticatedCallInvoker>();
    }

    [Fact]
    public void CreateCallInvoker_returns_the_authenticated_invoker_on_AdminClient()
    {
        using var channel = GrpcChannel.ForAddress(Options.GrpcAddress);
        using var client = CreateAdminClient(channel);

        client.CreateCallInvoker().Should().BeOfType<AuthenticatedCallInvoker>();
    }

    [Fact]
    public void CreateCallInvoker_throws_ObjectDisposedException_once_the_LedgerClient_is_disposed()
    {
        using var channel = GrpcChannel.ForAddress(Options.GrpcAddress);
        var client = CreateLedgerClient(channel);
        client.Dispose();

        var act = () => client.CreateCallInvoker();

        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void CreateCallInvoker_throws_ObjectDisposedException_once_the_AdminClient_is_disposed()
    {
        using var channel = GrpcChannel.ForAddress(Options.GrpcAddress);
        var client = CreateAdminClient(channel);
        client.Dispose();

        var act = () => client.CreateCallInvoker();

        act.Should().Throw<ObjectDisposedException>();
    }
}
