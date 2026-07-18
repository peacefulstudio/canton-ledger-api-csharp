// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public class UserRightTests
{
    [Fact]
    public void ActAs_right_contains_party()
    {
        var right = new UserRight.ActAs("Alice::1234");

        right.Party.Should().Be("Alice::1234");
    }

    [Fact]
    public void ReadAs_right_contains_party()
    {
        var right = new UserRight.ReadAs("Bob::5678");

        right.Party.Should().Be("Bob::5678");
    }

    [Fact]
    public void UserRight_equality_distinguishes_parties_and_party_variants()
    {
        UserRight aliceActAs = new UserRight.ActAs("Alice::1234");

        aliceActAs.Should().Be(new UserRight.ActAs("Alice::1234"));
        aliceActAs.Should().NotBe(new UserRight.ActAs("Bob::5678"));
        aliceActAs.Should().NotBe(new UserRight.ReadAs("Alice::1234"),
            "ActAs and ReadAs are distinct rights even for the same party");
    }

    [Fact]
    public void UserRight_equality_distinguishes_admin_marker_variants()
    {
        UserRight participantAdmin = new UserRight.ParticipantAdmin();

        participantAdmin.Should().Be(new UserRight.ParticipantAdmin());
        participantAdmin.Should().NotBe(new UserRight.IdentityProviderAdmin(),
            "ParticipantAdmin and IdentityProviderAdmin are distinct rights, not interchangeable markers");
    }
}
