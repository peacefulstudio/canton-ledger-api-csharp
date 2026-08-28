// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Rest.Client.Raw;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RightSerializationTests
{
    private const string Alice = "alice::ns1";

    [Fact]
    public void Right_nests_a_participant_admin_right_under_kind()
    {
        var right = new Right { Kind = new RightKind { ParticipantAdmin = new Right_ParticipantAdmin() } };

        var json = JsonSerializer.Serialize(right, RestRefitSettings.SerializerOptions);

        json.Should().Be("""{"kind":{"ParticipantAdmin":{"value":{}}}}""");
    }

    [Fact]
    public void Right_nests_an_act_as_right_under_kind()
    {
        var right = new Right { Kind = new RightKind { CanActAs = new Right_CanActAs { Party = Alice } } };

        var json = JsonSerializer.Serialize(right, RestRefitSettings.SerializerOptions);

        json.Should().Be("""{"kind":{"CanActAs":{"value":{"party":"alice::ns1"}}}}""");
    }

    [Fact]
    public void Right_nests_an_execute_as_any_party_right_under_kind()
    {
        var right = new Right
        {
            Kind = new RightKind { CanExecuteAsAnyParty = new Right_CanExecuteAsAnyParty() },
        };

        var json = JsonSerializer.Serialize(right, RestRefitSettings.SerializerOptions);

        json.Should().Be("""{"kind":{"CanExecuteAsAnyParty":{"value":{}}}}""");
    }

    [Fact]
    public void Right_reads_a_served_read_as_right_through_kind()
    {
        var right = JsonSerializer.Deserialize<Right>(
            """{"kind":{"CanReadAs":{"value":{"party":"alice::ns1"}}}}""",
            RestRefitSettings.SerializerOptions);

        right.Should().NotBeNull();
        right.Kind.Should().NotBeNull();
        right.Kind.CanReadAs.Should().NotBeNull();
        right.Kind.CanReadAs.Party.Should().Be(Alice);
        right.Kind.CanActAs.Should().BeNull();
    }

    [Fact]
    public void Right_reads_a_served_identity_provider_admin_right_through_kind()
    {
        var right = JsonSerializer.Deserialize<Right>(
            """{"kind":{"IdentityProviderAdmin":{"value":{}}}}""",
            RestRefitSettings.SerializerOptions);

        right.Should().NotBeNull();
        right.Kind.Should().NotBeNull();
        right.Kind.IdentityProviderAdmin.Should().NotBeNull();
        right.Kind.IdentityProviderAdmin.AdditionalProperties.Should()
            .BeEmpty("the value level belongs to the arm's encoding, not to the right itself");
    }

    [Fact]
    public void Right_nests_an_execute_as_right_under_kind()
    {
        var right = new Right { Kind = new RightKind { CanExecuteAs = new Right_CanExecuteAs { Party = Alice } } };

        var json = JsonSerializer.Serialize(right, RestRefitSettings.SerializerOptions);

        json.Should().Be("""{"kind":{"CanExecuteAs":{"value":{"party":"alice::ns1"}}}}""");
    }

    [Fact]
    public void Right_reads_a_served_execute_as_right_through_kind()
    {
        var right = JsonSerializer.Deserialize<Right>(
            """{"kind":{"CanExecuteAs":{"value":{"party":"alice::ns1"}}}}""",
            RestRefitSettings.SerializerOptions);

        right.Should().NotBeNull();
        right.Kind.Should().NotBeNull();
        right.Kind.CanExecuteAs.Should().NotBeNull();
        right.Kind.CanExecuteAs.Party.Should().Be(Alice);
    }

    [Fact]
    public void Right_leaves_every_arm_null_when_the_served_empty_arm_arrives()
    {
        var right = JsonSerializer.Deserialize<Right>(
            """{"kind":{"Empty":{}}}""",
            RestRefitSettings.SerializerOptions);

        right.Should().NotBeNull();
        right.Kind.Should().NotBeNull();
        right.Kind.ParticipantAdmin.Should().BeNull();
        right.Kind.CanActAs.Should().BeNull();
        right.Kind.CanReadAs.Should().BeNull();
        right.Kind.CanExecuteAs.Should().BeNull();
        right.Kind.CanExecuteAsAnyParty.Should().BeNull();
        right.Kind.CanReadAsAnyParty.Should().BeNull();
        right.Kind.IdentityProviderAdmin.Should().BeNull();

        JsonSerializer.Serialize(right, RestRefitSettings.SerializerOptions).Should()
            .Be("""{"kind":{"Empty":{}}}""", "the unmodelled arm survives a round trip through the extension bag");
    }

    [Fact]
    public void Right_rejects_two_kinds_selected_at_once()
    {
        var right = new Right
        {
            Kind = new RightKind
            {
                ParticipantAdmin = new Right_ParticipantAdmin(),
                CanActAs = new Right_CanActAs { Party = Alice },
            },
        };

        var act = () => JsonSerializer.Serialize(right, RestRefitSettings.SerializerOptions);

        act.Should().Throw<JsonException>().WithMessage("*exactly one arm*");
    }
}
