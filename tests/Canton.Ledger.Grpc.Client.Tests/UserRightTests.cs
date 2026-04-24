// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using FluentAssertions;
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
    public void ParticipantAdmin_right_can_be_created()
    {
        var right = new UserRight.ParticipantAdmin();

        right.Should().BeOfType<UserRight.ParticipantAdmin>();
    }

    [Fact]
    public void IdentityProviderAdmin_right_can_be_created()
    {
        var right = new UserRight.IdentityProviderAdmin();

        right.Should().BeOfType<UserRight.IdentityProviderAdmin>();
    }

    [Fact]
    public void UserRight_supports_equality()
    {
        var right1 = new UserRight.ActAs("Alice::1234");
        var right2 = new UserRight.ActAs("Alice::1234");
        var right3 = new UserRight.ActAs("Bob::5678");

        right1.Should().Be(right2);
        right1.Should().NotBe(right3);
    }
}
