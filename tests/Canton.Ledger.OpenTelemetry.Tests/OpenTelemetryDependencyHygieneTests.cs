// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Linq;
using AwesomeAssertions;
using OpenTelemetry.Trace;
using Xunit;

namespace Canton.Ledger.OpenTelemetry.Tests;

public class OpenTelemetryDependencyHygieneTests
{
    private static readonly string?[] ReferencedAssemblyNames =
        typeof(CantonLedgerTracerProviderBuilderExtensions).Assembly
            .GetReferencedAssemblies()
            .Select(assemblyName => assemblyName.Name)
            .ToArray();

    [Theory]
    [InlineData("Canton.Ledger.Grpc")]
    [InlineData("Canton.Ledger.Pqs")]
    [InlineData("Canton.Ledger.Rest")]
    public void CantonLedgerOpenTelemetry_references_no_concrete_client_assembly(string forbiddenPrefix) =>
        ReferencedAssemblyNames.Should().NotContain(
            name => name!.StartsWith(forbiddenPrefix, StringComparison.Ordinal),
            "Canton.Ledger.OpenTelemetry must reach the well-known ActivitySource names through Canton.Ledger.Kernel alone");

    [Fact]
    public void CantonLedgerOpenTelemetry_references_the_kernel() =>
        ReferencedAssemblyNames.Should().Contain("Canton.Ledger.Kernel");
}
