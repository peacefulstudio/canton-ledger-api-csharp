// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net.Http;
using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public class LedgerGrpcChannelTests
{
    private static LedgerClientOptions NewOptions() =>
        new() { GrpcAddress = "https://localhost:5001" };

    [Fact]
    public void BuildOptions_applies_the_default_keepalive_to_a_SocketsHttpHandler()
    {
        var handler = HandlerFrom(NewOptions());

        handler.KeepAlivePingDelay.Should().Be(TimeSpan.FromSeconds(60));
        handler.KeepAlivePingTimeout.Should().Be(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public void BuildOptions_carries_the_configured_keepalive_intervals_onto_the_handler()
    {
        var options = NewOptions();
        options.KeepAlivePingDelay = TimeSpan.FromSeconds(15);
        options.KeepAlivePingTimeout = TimeSpan.FromSeconds(5);

        var handler = HandlerFrom(options);

        handler.KeepAlivePingDelay.Should().Be(TimeSpan.FromSeconds(15));
        handler.KeepAlivePingTimeout.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void BuildOptions_owns_and_disposes_the_default_handler_it_created()
    {
        var channelOptions = LedgerGrpcChannel.BuildOptions(NewOptions());

        channelOptions.DisposeHttpClient.Should().BeTrue();
    }

    [Fact]
    public void BuildOptions_preserves_the_MaxMessageSize_on_both_directions()
    {
        var options = NewOptions();
        options.MaxMessageSize = 7 * 1024 * 1024;

        var channelOptions = LedgerGrpcChannel.BuildOptions(options);

        channelOptions.MaxReceiveMessageSize.Should().Be(7 * 1024 * 1024);
        channelOptions.MaxSendMessageSize.Should().Be(7 * 1024 * 1024);
    }

    [Fact]
    public void BuildOptions_honors_a_ConfigureChannel_handler_override()
    {
        var replacement = new SocketsHttpHandler();
        var options = NewOptions();
        options.ConfigureChannel = channel => channel.HttpHandler = replacement;

        var channelOptions = LedgerGrpcChannel.BuildOptions(options);

        channelOptions.HttpHandler.Should().BeSameAs(replacement);
    }

    [Fact]
    public void BuildOptions_runs_ConfigureChannel_after_the_defaults_so_the_hook_wins()
    {
        var options = NewOptions();
        options.ConfigureChannel = channel => channel.MaxReceiveMessageSize = 42;

        var channelOptions = LedgerGrpcChannel.BuildOptions(options);

        channelOptions.MaxReceiveMessageSize.Should().Be(42);
    }

    private static SocketsHttpHandler HandlerFrom(LedgerClientOptions options) =>
        LedgerGrpcChannel.BuildOptions(options).HttpHandler
            .Should().BeOfType<SocketsHttpHandler>().Subject;
}
