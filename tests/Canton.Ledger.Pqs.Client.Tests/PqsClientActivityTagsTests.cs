// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Pqs.Client.Tests;

public class PqsClientActivityTagsTests
{
    [Fact]
    public void DamlTemplateId_pins_the_canonical_semconv_name() =>
        PqsClientActivityTags.DamlTemplateId.Should().Be("daml.template_id");

    [Fact]
    public void CantonPqsResultCount_pins_the_canonical_semconv_name() =>
        PqsClientActivityTags.CantonPqsResultCount.Should().Be("canton.pqs.result_count");
}
