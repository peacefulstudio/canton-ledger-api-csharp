// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// Pins that one wire <c>category</c> value classifies to one <see cref="DamlErrorCategory"/>
/// whatever transport carried it. Both <c>DamlErrorParser</c> (gRPC trailers) and
/// <c>RestErrorParser</c> (JSON body) are <c>internal</c> and this project has no
/// <c>InternalsVisibleTo</c> into either client, so the transports' own parse paths are asserted
/// against these same rows by twin tests in the per-transport unit suites
/// (<c>DamlErrorParserTests</c> and <c>RestErrorParserTests</c>); what this suite pins is the
/// single shared classifier they both delegate to.
/// </summary>
public class LedgerErrorClassificationParityTests
{
    public static TheoryData<string, DamlErrorCategory> WireCategories() => new()
    {
        { "8", DamlErrorCategory.InvalidIndependentOfSystemState },
        { "11", DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing },
        { "ContentionOnSharedResources", DamlErrorCategory.ContentionOnSharedResources },
        { "50", DamlErrorCategory.Unknown },
        { "-1", DamlErrorCategory.Unknown },
        { "TotallyMadeUpCategory", DamlErrorCategory.Unknown },
        { "TransientServerFailure,ContentionOnSharedResources", DamlErrorCategory.Unknown },
    };

    [Theory]
    [MemberData(nameof(WireCategories))]
    public void MapCategory_classifies_a_wire_category_the_same_way_for_every_transport(
        string wireCategory, DamlErrorCategory expected)
    {
        ParsedLedgerError.MapCategory(wireCategory).Should().Be(expected);
    }
}
