// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Rest.Client.Tests;

internal sealed record CirceMarker(
    [property: DamlFieldAttribute("owner")] Party Owner,
    [property: DamlFieldAttribute("amount")] decimal Amount) : ITemplate, IDamlRecord<CirceMarker>
{
    public static RuntimeIdentifier TemplateId { get; } = new("tmpl-pkg", "Sample.Token", "Holding");

    public static string PackageId => "tmpl-pkg";

    public static string PackageName => "token-impl";

    public static Version PackageVersion { get; } = new(0, 1, 0);

    public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

    public DamlRecord ToRecord() => DamlRecord.Create(
        DamlField.Create("owner", Owner.ToDamlValue()),
        DamlField.Create("amount", new DamlNumeric(Amount)));

    public static CirceMarker FromRecord(DamlRecord record) => new(
        Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()),
        record.GetRequiredField("amount").As<DamlNumeric>().Value);
}
