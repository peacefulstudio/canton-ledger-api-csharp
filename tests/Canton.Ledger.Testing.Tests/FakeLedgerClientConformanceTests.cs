// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions;
using Daml.Ledger.Abstractions.Testing.Conformance;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;

namespace Canton.Ledger.Testing.Tests;

public class FakeLedgerClientConformanceTests : LedgerClientConformanceTests<ConformanceProbe>
{
    private static readonly Party Alice = new("alice");
    private static readonly SynchronizerId Synchronizer = (SynchronizerId)"sync-1";
    private static readonly ContractId<ConformanceProbe> Probe = new("00probe");
    private static readonly ConformanceProbe Payload = new(Alice);
    private static readonly LedgerOffset Created = LedgerOffset.At(1);
    private static readonly LedgerOffset Unclassifiable = LedgerOffset.At(2);
    private static readonly LedgerOffset Consumed = LedgerOffset.At(3);
    private static readonly LedgerOffset LedgerEnd = LedgerOffset.At(5);

    protected override SubmitterInfo Reader { get; } = Alice;

    protected override ILedgerClient CreateClient() =>
        FakeLedgerClient.Create()
            .WithLedgerEnd(LedgerEnd)
            .WithActiveContracts(
                LedgerEvents.Created(Probe, Payload, null, Created, Synchronizer, [Alice]),
                LedgerEvents.Unclassified<ConformanceProbe>(Unclassifiable, UnclassifiedKind.MissingSynchronizerId),
                LedgerEvents.Checkpoint<ConformanceProbe>(LedgerEnd))
            .WithContractEvents(
                ContractEvents.Created(Probe, Payload, null, Created, Synchronizer, [Alice]),
                ContractEvents.Archived(Probe, Consumed, Synchronizer, [Alice]))
            .WithLedgerEffects(
                ContractEvents.Created(Probe, Payload, null, Created, Synchronizer, [Alice]),
                ContractEvents.Exercised(
                    Probe,
                    "Archive",
                    DamlRecord.Create(),
                    DamlUnit.Instance,
                    consuming: true,
                    Consumed,
                    Synchronizer,
                    [Alice]))
            .Build();

    protected override ILedgerClient CreateFaultingSnapshotClient() =>
        FakeLedgerClient.Create()
            .WithActiveContracts(LedgerEvents.StreamError<ConformanceProbe>(14, "snapshot aborted mid-stream"))
            .Build();

    protected override CommandIdConformanceFixture? CreateCommandIdFixture()
    {
        var client = FakeLedgerClient.Create()
            .WithExerciseResult(LedgerOutcomes.One(DamlUnit.Instance))
            .WithCreateResult(LedgerOutcomes.One(Probe))
            .Build();

        return new CommandIdConformanceFixture(
            client,
            (writer, commandId) => writer.TryExerciseAsync<DamlUnit>(ArchiveProbe, Alice, commandId: commandId),
            (writer, commandId) => writer.TryCreateAsync(new ConformanceProbe(Alice), Alice, commandId: commandId),
            () => ValueTask.FromResult(client.LastSubmittedCommandId?.Value));
    }

    private static readonly ExerciseCommand ArchiveProbe = new(
        ConformanceProbe.TemplateId, Probe, new ChoiceName("Archive"), DamlRecord.Create());
}

/// <summary>The Daml marker the conformance scenario's snapshot and streams are filtered to.</summary>
/// <param name="Owner">The party the probe contract is issued to.</param>
public sealed record ConformanceProbe(Party Owner) : ITemplate, IDamlRecord<ConformanceProbe>
{
    /// <inheritdoc cref="ITemplate" />
    public static Identifier TemplateId { get; } = new("conformance-pkg", "Conformance.Probe", "Probe");

    /// <inheritdoc cref="ITemplate" />
    public static string PackageId => "conformance-pkg";

    /// <inheritdoc cref="ITemplate" />
    public static string PackageName => "conformance-package";

    /// <inheritdoc cref="ITemplate" />
    public static Version PackageVersion { get; } = new(0, 1, 0);

    /// <inheritdoc cref="ITemplate" />
    public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

    /// <inheritdoc cref="ITemplate" />
    public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("owner", new DamlParty((string)Owner)));

    /// <summary>Creates a probe from the wire record a created event carries.</summary>
    /// <returns>The probe the record decodes to.</returns>
    public static ConformanceProbe FromRecord(DamlRecord record) =>
        new((Party)record.GetRequiredField("owner").As<DamlParty>().Value);
}
