// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Kernel.Streams;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Xunit;

namespace Canton.Ledger.Kernel.Tests.Streams;

public class SubscribeFilterPolicyTests
{
    [Fact]
    public void FilteredPartyIds_covers_actAs_before_readAs()
    {
        var submitter = new SubmitterInfo(
            new HashSet<Party> { (Party)"alice" },
            new HashSet<Party> { (Party)"observer" });

        SubscribeFilterPolicy.FilteredPartyIds(submitter).Should().Equal("alice", "observer");
    }

    [Fact]
    public void FilteredPartyIds_yields_a_party_reading_and_acting_only_once()
    {
        var submitter = new SubmitterInfo(
            new HashSet<Party> { (Party)"alice" },
            new HashSet<Party> { (Party)"alice", (Party)"observer" });

        SubscribeFilterPolicy.FilteredPartyIds(submitter).Should().Equal("alice", "observer");
    }

    [Fact]
    public void FilteredPartyIds_covers_actAs_alone_when_the_submitter_reads_as_nobody()
    {
        var submitter = new SubmitterInfo(
            new HashSet<Party> { (Party)"alice" },
            new HashSet<Party>());

        SubscribeFilterPolicy.FilteredPartyIds(submitter).Should().Equal("alice");
    }
}
