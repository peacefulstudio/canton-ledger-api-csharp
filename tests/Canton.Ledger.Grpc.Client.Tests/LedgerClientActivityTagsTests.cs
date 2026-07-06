// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public class LedgerClientActivityTagsTests
{
    [Fact]
    public void DamlChoice_pins_the_canonical_semconv_name() =>
        LedgerClientActivityTags.DamlChoice.Should().Be("daml.choice");

    [Fact]
    public void DamlContractId_pins_the_canonical_semconv_name() =>
        LedgerClientActivityTags.DamlContractId.Should().Be("daml.contract_id");

    [Fact]
    public void DamlTemplateId_pins_the_canonical_semconv_name() =>
        LedgerClientActivityTags.DamlTemplateId.Should().Be("daml.template_id");

    [Fact]
    public void DamlPackageId_pins_the_canonical_semconv_name() =>
        LedgerClientActivityTags.DamlPackageId.Should().Be("daml.package_id");

    [Fact]
    public void CantonOffset_pins_the_canonical_semconv_name() =>
        LedgerClientActivityTags.CantonOffset.Should().Be("canton.offset");

    [Fact]
    public void CantonFromOffset_pins_the_canonical_semconv_name() =>
        LedgerClientActivityTags.CantonFromOffset.Should().Be("canton.from_offset");

    [Fact]
    public void CantonSubmitterActAs_pins_the_canonical_semconv_name() =>
        LedgerClientActivityTags.CantonSubmitterActAs.Should().Be("canton.submitter.act_as");

    [Fact]
    public void CantonSubmitterReadAs_pins_the_canonical_semconv_name() =>
        LedgerClientActivityTags.CantonSubmitterReadAs.Should().Be("canton.submitter.read_as");

    [Fact]
    public void CantonUpdateId_pins_the_canonical_semconv_name() =>
        LedgerClientActivityTags.CantonUpdateId.Should().Be("canton.update_id");

    [Fact]
    public void CantonPartyId_pins_the_canonical_semconv_name() =>
        LedgerClientActivityTags.CantonPartyId.Should().Be("canton.party_id");

    [Fact]
    public void CantonParticipantId_pins_the_canonical_semconv_name() =>
        LedgerClientActivityTags.CantonParticipantId.Should().Be("canton.participant_id");

    [Fact]
    public void CantonPartyIdHint_pins_the_canonical_semconv_name() =>
        LedgerClientActivityTags.CantonPartyIdHint.Should().Be("canton.party_id_hint");

    [Fact]
    public void CantonUserId_pins_the_canonical_semconv_name() =>
        LedgerClientActivityTags.CantonUserId.Should().Be("canton.user_id");

    [Fact]
    public void CantonSubmissionId_pins_the_canonical_semconv_name() =>
        LedgerClientActivityTags.CantonSubmissionId.Should().Be("canton.submission_id");
}
