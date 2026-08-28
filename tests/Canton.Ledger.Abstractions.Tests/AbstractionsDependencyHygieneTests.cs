// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Linq;
using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Abstractions.Tests;

public class AbstractionsDependencyHygieneTests
{
    [Theory]
    [InlineData("Google.Protobuf")]
    [InlineData("Grpc.")]
    [InlineData("Canton.Ledger.Grpc")]
    [InlineData("Npgsql")]
    public void CantonLedgerAbstractions_references_no_transport_or_protobuf_assembly(string forbiddenPrefix)
    {
        var referencedAssemblyNames = typeof(Canton.Ledger.Abstractions.ConnectedSynchronizer).Assembly
            .GetReferencedAssemblies()
            .Select(assemblyName => assemblyName.Name)
            .ToArray();

        referencedAssemblyNames.Should().NotContain(
            name => name!.StartsWith(forbiddenPrefix, StringComparison.Ordinal),
            "Canton.Ledger.Abstractions must stay transport-neutral");
    }
}
