// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Daml.Ledger.Abstractions;
using Daml.Ledger.Abstractions.Testing.Conformance;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Rest.Client.Tests;

public class RestLedgerClientConformanceTests : LedgerClientConformanceTests<RestConformanceProbe>
{
    protected override SubmitterInfo Reader { get; } = new Party("party::alice");

    protected override ILedgerClient CreateClient() =>
        new RestLedgerClient(new ParticipantHttpClientFactory(new ConformanceParticipantHandler()));

    private sealed class ParticipantHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://localhost:7575") };
    }
}

/// <summary>The Daml marker the conformance scenario's snapshot and streams are filtered to.</summary>
/// <param name="Owner">The party the probe contract is issued to.</param>
public sealed record RestConformanceProbe(string Owner) : ITemplate
{
    /// <inheritdoc cref="ITemplate" />
    public static RuntimeIdentifier TemplateId { get; } = new("conformance-pkg", "Conformance.Probe", "Probe");

    /// <inheritdoc cref="ITemplate" />
    public static string PackageId => "conformance-pkg";

    /// <inheritdoc cref="ITemplate" />
    public static string PackageName => "conformance-package";

    /// <inheritdoc cref="ITemplate" />
    public static Version PackageVersion { get; } = new(0, 1, 0);

    /// <inheritdoc cref="ITemplate" />
    public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

    /// <inheritdoc cref="ITemplate" />
    public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("owner", new DamlParty(Owner)));
}

/// <summary>
/// The participant half of the JSON Ledger API conformance scenario: it serves the seeded ledger
/// end, filters the active-contract snapshot by the requested <c>activeAtOffset</c>, and filters
/// the update stream by the requested <c>(beginExclusive, endInclusive]</c> window and transaction
/// shape — so the conformance checks exercise what <see cref="RestLedgerClient"/> asks for as well
/// as how it projects the answer.
/// </summary>
internal sealed class ConformanceParticipantHandler : HttpMessageHandler
{
    private const string LedgerEndPath = "/v2/state/ledger-end";
    private const string ActiveContractsPath = "/v2/state/active-contracts";
    private const string UpdatesPath = "/v2/updates";
    private const string LedgerEffectsShape = "TRANSACTION_SHAPE_LEDGER_EFFECTS";
    private const string AcsDeltaShape = "TRANSACTION_SHAPE_ACS_DELTA";
    private const long CreatedOffset = 1L;
    private const long UnclassifiableOffset = 2L;
    private const long ConsumedOffset = 3L;
    private const long LedgerEndOffset = 5L;
    private const string Synchronizer = "sync-1";

    private static readonly object ProbeTemplateId =
        new { packageId = "conformance-pkg", moduleName = "Conformance.Probe", entityName = "Probe" };

    private static readonly object ForeignTemplateId =
        new { packageId = "conformance-pkg", moduleName = "Conformance.Other", entityName = "Other" };

    private static readonly string[] Witnesses = ["party::alice"];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        object payload = request.RequestUri!.AbsolutePath switch
        {
            LedgerEndPath => new { offset = LedgerEndOffset },
            ActiveContractsPath => ActiveContractsAt(OffsetField(body, "activeAtOffset")),
            UpdatesPath => UpdatesFor(body),
            var unexpected => throw new InvalidOperationException(
                $"The conformance scenario seeds no response for '{unexpected}'."),
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
    }

    private static object[] ActiveContractsAt(long activeAtOffset) =>
        [
            .. new[]
            {
                (Offset: CreatedOffset, Row: ActiveContract("00probe", ProbeTemplateId, CreatedOffset)),
                (Offset: UnclassifiableOffset, Row: ActiveContract("00foreign", ForeignTemplateId, UnclassifiableOffset)),
            }
            .Where(seeded => seeded.Offset <= activeAtOffset)
            .Select(seeded => seeded.Row),
        ];

    private static object[] UpdatesFor(string body)
    {
        var beginExclusive = OffsetField(body, "beginExclusive");
        var endInclusive = OffsetField(body, "endInclusive");
        var consumption = RequestsLedgerEffects(body) ? ConsumingExercise() : Archival();

        return
        [
            .. new[] { (Offset: CreatedOffset, Event: Creation()), (Offset: ConsumedOffset, Event: consumption) }
                .Where(seeded => seeded.Offset > beginExclusive && seeded.Offset <= endInclusive)
                .Select(seeded => Transaction(seeded.Offset, seeded.Event)),
        ];
    }

    private static bool RequestsLedgerEffects(string body)
    {
        if (body.Contains(LedgerEffectsShape, StringComparison.Ordinal))
        {
            return true;
        }

        if (body.Contains(AcsDeltaShape, StringComparison.Ordinal))
        {
            return false;
        }

        throw new InvalidOperationException(
            $"The update request names neither '{AcsDeltaShape}' nor '{LedgerEffectsShape}': {body}");
    }

    private static long OffsetField(string body, string name)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty(name, out var offset)
            ? long.Parse(offset.GetString()!, CultureInfo.InvariantCulture)
            : throw new InvalidOperationException(
                $"The request omits the offset field '{name}', so the scenario cannot bound it: {body}");
    }

    private static object ActiveContract(string contractId, object templateId, long offset) => new
    {
        contractEntry = new
        {
            JsActiveContract = new
            {
                createdEvent = CreatedEvent(contractId, templateId, offset),
                synchronizerId = Synchronizer,
            },
        },
    };

    private static object Transaction(long offset, object seeded) => new
    {
        update = new
        {
            Transaction = new
            {
                value = new
                {
                    offset = offset.ToString(CultureInfo.InvariantCulture),
                    synchronizerId = Synchronizer,
                    events = new[] { seeded },
                },
            },
        },
    };

    private static object CreatedEvent(string contractId, object templateId, long offset) => new
    {
        offset = offset.ToString(CultureInfo.InvariantCulture),
        contractId,
        templateId,
        createArgument = new { fields = Array.Empty<object>() },
        witnessParties = Witnesses,
    };

    private static object Creation() => new { CreatedEvent = CreatedEvent("00probe", ProbeTemplateId, CreatedOffset) };

    private static object Archival() => new
    {
        ArchivedEvent = new
        {
            offset = ConsumedOffset.ToString(CultureInfo.InvariantCulture),
            contractId = "00probe",
            templateId = ProbeTemplateId,
            witnessParties = Witnesses,
        },
    };

    private static object ConsumingExercise() => new
    {
        ExercisedEvent = new
        {
            offset = ConsumedOffset.ToString(CultureInfo.InvariantCulture),
            contractId = "00probe",
            templateId = ProbeTemplateId,
            choice = "Archive",
            choiceArgument = new { record = new { fields = Array.Empty<object>() } },
            actingParties = Witnesses,
            consuming = true,
            witnessParties = Witnesses,
            exerciseResult = new { unit = new { } },
        },
    };
}
