// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Rest.Client.Tests;

public class MarkerMatcherTests
{
    private sealed record ScalarKeyedMarker(Party Owner)
        : ITemplate, IDamlRecord<ScalarKeyedMarker>, IHasKey<ScalarKeyedMarker, Party>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("tmpl-pkg", "Sample.Token", "Steward");
        public static string PackageId => "tmpl-pkg";
        public static string PackageName => "token-impl";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public static KeyDescriptor<ScalarKeyedMarker, Party> Key { get; } = new()
        {
            KeyEncoder = owner => owner.ToDamlValue(),
            KeyDecoder = value => Party.FromDamlValue(value.As<DamlParty>()),
        };

        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("owner", Owner.ToDamlValue()));

        public static ScalarKeyedMarker FromRecord(DamlRecord record) =>
            new(Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()));
    }

    private sealed record AccountKey(Party Custodian, string Label);

    private sealed record RecordKeyedMarker(Party Custodian, string Label)
        : ITemplate, IDamlRecord<RecordKeyedMarker>, IHasKey<RecordKeyedMarker, AccountKey>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("tmpl-pkg", "Sample.Token", "Account");
        public static string PackageId => "tmpl-pkg";
        public static string PackageName => "token-impl";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public static KeyDescriptor<RecordKeyedMarker, AccountKey> Key { get; } = new()
        {
            KeyEncoder = key => DamlRecord.Create(
                DamlField.Create("custodian", key.Custodian.ToDamlValue()),
                DamlField.Create("label", new DamlText(key.Label))),
            KeyDecoder = value =>
            {
                var record = value.As<DamlRecord>();
                return new AccountKey(
                    Party.FromDamlValue(record.GetRequiredField("custodian").As<DamlParty>()),
                    record.GetRequiredField("label").As<DamlText>().Value);
            },
        };

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("custodian", Custodian.ToDamlValue()),
            DamlField.Create("label", new DamlText(Label)));

        public static RecordKeyedMarker FromRecord(DamlRecord record) => new(
            Party.FromDamlValue(record.GetRequiredField("custodian").As<DamlParty>()),
            record.GetRequiredField("label").As<DamlText>().Value);
    }

    private sealed record UnkeyedMarker : ITemplate, IDamlRecord<UnkeyedMarker>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("tmpl-pkg", "Sample.Token", "Holding");
        public static string PackageId => "tmpl-pkg";
        public static string PackageName => "token-impl";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => new(TemplateId, []);

        public static UnkeyedMarker FromRecord(DamlRecord record) => new();
    }

    private sealed record InterfaceMarker : IDamlInterface
    {
        public static RuntimeIdentifier InterfaceId { get; } = new("iface-pkg", "Sample.Token", "IHolding");
        public static string PackageId => "iface-pkg";
        public static string PackageName => "token-iface";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(InterfaceId, DamlTypeKind.Interface, PackageName);
        public DamlRecord ToRecord() => new(InterfaceId, []);
    }

    [Fact]
    public void KeyType_is_the_bare_scalar_key_type_of_a_marker_keyed_by_a_bare_scalar()
    {
        MarkerMatcher<ScalarKeyedMarker>.KeyType.Should().Be<Party>();
    }

    [Fact]
    public void KeyType_is_the_key_record_type_of_a_marker_keyed_by_a_record()
    {
        MarkerMatcher<RecordKeyedMarker>.KeyType.Should().Be<AccountKey>();
    }

    [Fact]
    public void KeyType_is_null_for_an_unkeyed_template_marker()
    {
        MarkerMatcher<UnkeyedMarker>.KeyType.Should().BeNull();
    }

    [Fact]
    public void KeyType_is_null_for_an_interface_marker()
    {
        MarkerMatcher<InterfaceMarker>.KeyType.Should().BeNull();
    }
}
