// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Xunit;

namespace Canton.Ledger.Testing.Helpers;

/// <summary>
/// Behavioural parity suite over the contract key each projection path builds, run against every
/// transport's paths through one shared assertion. Each path decodes the same wire created event
/// into its own <see cref="ContractKey"/>, so a path that reads the key but not the key hash keeps
/// projecting a key that looks complete while dropping what by-key matching and
/// <c>exerciseByKey</c> read — and no other path is any the wiser.
/// </summary>
public abstract class ContractKeyHashParityTests
{
    /// <summary>The party the wire created event carries as its contract key.</summary>
    protected const string KeyParty = "alice::ns1";

    /// <summary>
    /// The key hash every path must project. Each transport carries it in its own wire encoding —
    /// the raw bytes over gRPC, this base64 text itself over REST.
    /// </summary>
    protected const string ProjectedKeyHash = "AQID";

    /// <summary>The bytes <see cref="ProjectedKeyHash"/> is the base64 encoding of.</summary>
    protected static ReadOnlySpan<byte> KeyHashBytes => [0x01, 0x02, 0x03];

    /// <summary>
    /// Projects the one keyed created event this transport puts on the wire through
    /// <paramref name="path"/>, and returns the contract key that path built.
    /// </summary>
    protected abstract ContractKey? ProjectedKeyOn(ContractKeyProjectionPath path);

    [Theory]
    [InlineData(ContractKeyProjectionPath.ContractStream)]
    [InlineData(ContractKeyProjectionPath.InterfaceStream)]
    [InlineData(ContractKeyProjectionPath.TransactionResult)]
    [InlineData(ContractKeyProjectionPath.TransactionTree)]
    public void Every_projection_path_carries_the_wire_key_hash_onto_the_projected_key(
        ContractKeyProjectionPath path)
    {
        var projected = ProjectedKeyOn(path);

        projected.Should().NotBeNull();
        projected.Should().Be(new ContractKey(new DamlParty(KeyParty), TemplateMarker.TemplateId));
        projected!.KeyHash.Should().Be(ProjectedKeyHash);
    }
}
