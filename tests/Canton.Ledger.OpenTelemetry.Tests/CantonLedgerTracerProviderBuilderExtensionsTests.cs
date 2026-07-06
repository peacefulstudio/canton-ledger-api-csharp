// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using AwesomeAssertions;
using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Pqs.Client;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Xunit;

namespace Canton.Ledger.OpenTelemetry.Tests;

public class CantonLedgerTracerProviderBuilderExtensionsTests
{
    [Fact]
    public void AddCantonLedgerInstrumentation_throws_when_builder_is_null()
    {
        TracerProviderBuilder builder = null!;

        var act = () => builder.AddCantonLedgerInstrumentation();

        act.Should().Throw<ArgumentNullException>().WithParameterName("builder");
    }

    [Fact]
    public void AddCantonLedgerInstrumentation_returns_the_same_builder_for_chaining()
    {
        var builder = Sdk.CreateTracerProviderBuilder();

        var result = builder.AddCantonLedgerInstrumentation();

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void AddCantonLedgerInstrumentation_registers_the_LedgerClient_ActivitySource()
    {
        var exportedItems = new List<Activity>();
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddCantonLedgerInstrumentation()
            .AddInMemoryExporter(exportedItems)
            .Build();

        using var source = new ActivitySource(LedgerClient.ActivitySourceName);
        using (source.StartActivity("test-ledger-client"))
        {
        }
        provider.ForceFlush();

        exportedItems.Should().ContainSingle(a => a.OperationName == "test-ledger-client");
    }

    [Fact]
    public void AddCantonLedgerInstrumentation_registers_the_AdminClient_ActivitySource()
    {
        var exportedItems = new List<Activity>();
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddCantonLedgerInstrumentation()
            .AddInMemoryExporter(exportedItems)
            .Build();

        using var source = new ActivitySource(AdminClient.ActivitySourceName);
        using (source.StartActivity("test-admin-client"))
        {
        }
        provider.ForceFlush();

        exportedItems.Should().ContainSingle(a => a.OperationName == "test-admin-client");
    }

    [Fact]
    public void AddCantonLedgerInstrumentation_registers_the_PqsClient_ActivitySource()
    {
        var exportedItems = new List<Activity>();
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddCantonLedgerInstrumentation()
            .AddInMemoryExporter(exportedItems)
            .Build();

        using var source = new ActivitySource(PqsClient.ActivitySourceName);
        using (source.StartActivity("test-pqs-client"))
        {
        }
        provider.ForceFlush();

        exportedItems.Should().ContainSingle(a => a.OperationName == "test-pqs-client");
    }

    [Fact]
    public void AddCantonLedgerInstrumentation_does_not_register_an_unrelated_ActivitySource()
    {
        var exportedItems = new List<Activity>();
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddCantonLedgerInstrumentation()
            .AddInMemoryExporter(exportedItems)
            .Build();

        using var source = new ActivitySource(nameof(AddCantonLedgerInstrumentation_does_not_register_an_unrelated_ActivitySource));
        using (source.StartActivity("unrelated"))
        {
        }
        provider.ForceFlush();

        exportedItems.Should().NotContain(a => a.OperationName == "unrelated");
    }
}
