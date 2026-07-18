// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions;
using AwesomeAssertions;
using NSubstitute;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public class ICantonLedgerClientTests
{
    [Fact]
    public void ICantonLedgerClient_extends_ILedgerClient()
    {
        typeof(ICantonLedgerClient).Should().BeAssignableTo<ILedgerClient>();
    }

    [Theory]
    [InlineData(nameof(ICantonLedgerClient.SubmitAsync))]
    [InlineData(nameof(ICantonLedgerClient.CompletionStreamAsync))]
    [InlineData(nameof(ICantonLedgerClient.GetConnectedSynchronizersAsync))]
    [InlineData(nameof(ICantonLedgerClient.GetLedgerApiVersionAsync))]
    [InlineData(nameof(ICantonLedgerClient.GetUpdateByOffsetAsync))]
    [InlineData(nameof(ICantonLedgerClient.GetUpdateByIdAsync))]
    public void ICantonLedgerClient_declares_the_operation_that_is_absent_from_ILedgerClient(string operation)
    {
        typeof(ICantonLedgerClient).GetMethods().Should().Contain(m => m.Name == operation,
            "the operation must be reachable through the DI-registered interface without downcasting to LedgerClient");
        typeof(ILedgerClient).GetMethods().Should().NotContain(m => m.Name == operation,
            "the operation is absent from the upstream ILedgerClient — that absence is the bug ICantonLedgerClient fixes");
    }

    [Fact]
    public async Task ICantonLedgerClient_is_mockable_without_the_concrete_LedgerClient()
    {
        ICantonLedgerClient client = Substitute.For<ICantonLedgerClient>();
        client.GetLedgerApiVersionAsync(Arg.Any<CancellationToken>()).Returns("3.4.11");

        var version = await client.GetLedgerApiVersionAsync(TestContext.Current.CancellationToken);

        version.Should().Be("3.4.11");
    }
}
