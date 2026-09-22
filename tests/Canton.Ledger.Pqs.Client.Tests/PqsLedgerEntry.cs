// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;

namespace Canton.Ledger.Pqs.Client.Tests;

internal sealed record PqsLedgerEntry(
    [property: DamlFieldAttribute("owner")] string Owner,
    [property: DamlFieldAttribute("quantity")] long Quantity,
    [property: DamlFieldAttribute("price")] decimal Price,
    [property: DamlFieldAttribute("note")] string? Note) : ITemplate, IDamlRecord<PqsLedgerEntry>
{
    public static Identifier TemplateId { get; } = new("pkg123", "Test.Module", "PqsLedgerEntry");
    public static string PackageId => "pkg123";
    public static string PackageName => "test-package";
    public static Version PackageVersion { get; } = new(0, 1, 0);
    public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

    public DamlRecord ToRecord() => throw new NotSupportedException();

    public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
        PqsRecordReader.Read(
            json,
            context,
            ("owner", DamlLfJsonDecoders.ReadParty),
            ("quantity", DamlLfJsonDecoders.ReadInt64),
            ("price", DamlLfJsonDecoders.ReadNumeric),
            ("note", PqsRecordReader.OptionalOf(DamlLfJsonDecoders.ReadText)));

    public static PqsLedgerEntry FromRecord(DamlRecord record) => new(
        Owner: record.GetRequiredField("owner").As<DamlParty>().Value,
        Quantity: record.GetRequiredField("quantity").As<DamlInt64>().Value,
        Price: record.GetRequiredField("price").As<DamlNumeric>().Value,
        Note: record.GetRequiredField("note").As<DamlOptional>().Value?.As<DamlText>().Value);
}
