// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Kernel.Commands;
using Xunit;

namespace Canton.Ledger.Kernel.Tests.Commands;

public class ReassignmentCommandPolicyTests
{
    [Fact]
    public void RequireNonEmpty_returns_a_populated_value_unchanged()
    {
        ReassignmentCommandPolicy.RequireNonEmpty("00contract", "unassign contract id")
            .Should().Be("00contract");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RequireNonEmpty_rejects_a_blank_value_naming_the_field(string blank)
    {
        var act = () => ReassignmentCommandPolicy.RequireNonEmpty(blank, "unassign contract id");

        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("unassign contract id");
    }

    [Fact]
    public void RequireNonEmpty_states_the_field_in_the_message()
    {
        var act = () => ReassignmentCommandPolicy.RequireNonEmpty("", "assign source synchronizer id");

        act.Should().Throw<ArgumentException>()
            .WithMessage("A reassignment requires a non-empty assign source synchronizer id.*");
    }
}
