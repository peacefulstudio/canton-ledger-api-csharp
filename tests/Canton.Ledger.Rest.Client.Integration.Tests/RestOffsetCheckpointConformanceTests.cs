// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Rest.Client.Raw;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Peaceful.Canton.Localnet.Testing;
using RichTypes;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Integration.Tests;

/// <summary>
/// LocalNet conformance coverage that the JSON transport relays the participant's own offset
/// checkpoints through a blocking window. It is what lets the pagination loop keep a consumer's
/// resume offset moving on a quiet ledger without the client manufacturing a checkpoint of its own,
/// and a transport that stripped them would leave the loop nothing to relay.
/// <para>
/// The measurement is bounded by the emission delay the participant advertises in its version
/// features, read at runtime rather than assumed, because before that delay elapses the participant
/// owes the stream nothing. It cannot be taken inside a single window: the participant's HTTP layer
/// answers a request it has held for twenty seconds with <c>503</c> and a "not able to produce a
/// timely response" body, well short of the seventy-five second delay LocalNet advertises. Windows
/// are therefore reopened from the same offset, exactly as the pagination loop reopens them, until
/// the checkpoint arrives or the advertised delay has passed with none.
/// </para>
/// <para>
/// The stream is filtered to a freshly allocated party, which no contract mentions, so an entry
/// carrying an event would mean the filter was discarded rather than that the ledger was busy.
/// Allocating that party is also what makes the first window liable to be answered
/// <c>409 STALE_STREAM_AUTHORIZATION</c>: the participant opened it against a topology snapshot the
/// allocation had already moved past, and asks for a quick retry. That status and the request
/// timeout above are reopened rather than asserted on, because neither is evidence either way about
/// what a window that does complete carries.
/// </para>
/// <para>
/// Reopening on that code is the caller's obligation, not a local accommodation this probe invented.
/// It is what <c>ICantonLedgerClient.CompletionStreamAsync</c>'s remarks now rule: the client carries
/// the participant's own error code to a terminal stream fault and retries no answered status itself,
/// leaving the reopen to the consumer, who is the one able to tell a self-clearing condition from a
/// fault a reopen reproduces. This probe is a consumer of that contract, so it identifies the
/// condition the way the client does — through <c>ParsedLedgerError.ReportedErrorId</c>, read by the
/// very parser the client reads its own faults with — rather than by matching the response body. It
/// stays at the wire level because it measures the raw entries a window carries and sets
/// <c>stream_idle_timeout_ms</c> per window, and the typed surface expresses neither; the typed
/// stream-fault code lives on the completion stream, which is not the read under measurement here.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
public class RestOffsetCheckpointConformanceTests(ITestOutputHelper output)
{
    private const string UpdatesPath = "/v2/updates";
    private const string VersionPath = "/v2/version";
    private const string FeaturesName = "features";
    private const string OffsetCheckpointSnakeCase = "offset_checkpoint";
    private const string OffsetCheckpointCamelCase = "offsetCheckpoint";
    private const string EmissionDelaySnakeCase = "max_offset_checkpoint_emission_delay";
    private const string EmissionDelayCamelCase = "maxOffsetCheckpointEmissionDelay";
    private const string DurationSecondsName = "seconds";
    private const string DurationNanosName = "nanos";
    private const long NanosecondsPerTick = 100L;
    private const string StaleStreamAuthorization = "STALE_STREAM_AUTHORIZATION";
    private const int UnmeasurableWindowAttempts = 4;

    /// <summary>
    /// How long one window asks the participant to hold. Kept under the twenty seconds after which
    /// the participant's HTTP layer answers the request <c>503</c> rather than closing the window.
    /// </summary>
    private static readonly TimeSpan WindowHold = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan DelayMargin = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RequestMargin = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan UnmeasurableWindowBackoff = TimeSpan.FromSeconds(2);

    private static string DarPath() => Path.Combine(
        AppContext.BaseDirectory, "testdata", "richtypes", "richtypes.dar");

    [Fact]
    public async Task Updates_windows_reopened_over_the_advertised_emission_delay_carry_an_offset_checkpoint()
    {
        await using var lane = await RestConformanceLane.OpenAsync(TestContext.Current.CancellationToken);

        var emissionDelay = await AdvertisedEmissionDelayAsync(lane, TestContext.Current.CancellationToken);
        var budget = emissionDelay + DelayMargin;
        output.WriteLine(
            $"participant advertises maxOffsetCheckpointEmissionDelay={emissionDelay}; reopening "
            + $"{UpdatesPath} windows of {WindowHold} for up to {budget}");

        await lane.Fixture.UploadDarAsync(DarPath(), TestContext.Current.CancellationToken);
        var party = await lane.Fixture.AllocatePartyAsync(
            "rest-offset-checkpoint", cancellationToken: TestContext.Current.CancellationToken);
        await lane.GrantActAsAsync(party.PartyId, TestContext.Current.CancellationToken);

        var quietParty = new Party(party.PartyId);
        var beginExclusive = await lane.LedgerClient.GetLedgerEndAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        var request = RestSubscribeRequestBuilder.BuildGetUpdatesRequest<Marker>(
            new SubmitterInfo(quietParty, new HashSet<Party>()),
            beginExclusive.Value,
            endInclusive: null,
            RestTransactionShape.AcsDelta);

        var observed = await FollowUntilCheckpointAsync(
            lane, request, budget, TestContext.Current.CancellationToken);

        output.WriteLine(
            $"{observed.Windows} window(s) over {observed.Elapsed} returned {observed.Entries.Count} entries");

        observed.Entries.Should().OnlyContain(
            entry => CarriesNoEvent(entry),
            "the stream is filtered to a party no contract mentions, so any event here means the filter was discarded");
        observed.Entries.Should().Contain(
            entry => IsOffsetCheckpoint(entry),
            "the participant relays its own offset checkpoints through a blocking JSON window, which is "
            + "the only thing that advances a consumer's resume offset on a quiet ledger");
    }

    /// <summary>
    /// The same emission delay the probe above budgets against, read through the typed client rather
    /// than off the wire — the reachability a consumer sizing its own resume loop depends on.
    /// </summary>
    [Fact]
    public async Task GetLedgerApiVersion_reads_MaxOffsetCheckpointEmissionDelay_through_the_typed_client()
    {
        await using var lane = await RestConformanceLane.OpenAsync(TestContext.Current.CancellationToken);

        var served = await AdvertisedEmissionDelayAsync(lane, TestContext.Current.CancellationToken);

        var version = await lane.Api<IVersionServiceApi>()
            .GetLedgerApiVersion(TestContext.Current.CancellationToken);

        version.Features.OffsetCheckpoint.Should().NotBeNull(
            "a consumer sizing its resume loop reads the advertised delay off the typed features "
            + "descriptor, not off a wire-level document it parses itself");
        version.Features.AdditionalProperties.Should().NotContainKey(
            OffsetCheckpointCamelCase,
            "the camelCase 'offsetCheckpoint' feature key must bind to the typed property");

        var advertised = version.Features.OffsetCheckpoint.MaxOffsetCheckpointEmissionDelay;
        output.WriteLine($"typed client read maxOffsetCheckpointEmissionDelay={advertised}");
        TryReadCanonicalDuration(advertised, out var typed).Should().BeTrue(
            $"the typed property carries the proto3-canonical duration, and it read {advertised}");
        typed.Should().Be(served, "the typed read and the wire read describe the same advertised delay");
    }

    private static async Task<WindowFollow> FollowUntilCheckpointAsync(
        RestConformanceLane lane, object request, TimeSpan budget, CancellationToken cancellationToken)
    {
        using var wireClient = lane.CreateWireLevelClient();
        wireClient.Timeout = WindowHold + RequestMargin;

        var entries = new List<JsonElement>();
        var windows = 0;
        var elapsed = Stopwatch.StartNew();
        do
        {
            entries.AddRange(await ReadOneWindowAsync(wireClient, request, cancellationToken));
            windows++;
        }
        while (!entries.Exists(IsOffsetCheckpoint) && elapsed.Elapsed < budget);

        return new WindowFollow(entries, windows, elapsed.Elapsed);
    }

    private static async Task<IReadOnlyList<JsonElement>> ReadOneWindowAsync(
        HttpClient wireClient, object request, CancellationToken cancellationToken)
    {
        var path = $"{UpdatesPath}?limit=200&stream_idle_timeout_ms="
            + ((long)WindowHold.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
        var body = JsonSerializer.Serialize(request, RestRefitSettings.SerializerOptions);

        var status = default(HttpStatusCode);
        var payload = string.Empty;
        for (var attempt = 0; attempt < UnmeasurableWindowAttempts; attempt++)
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await wireClient.PostAsync(new Uri(path, UriKind.Relative), content, cancellationToken);
            status = response.StatusCode;
            payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!await SaysNothingAboutCheckpointsAsync(response, cancellationToken))
            {
                break;
            }

            await Task.Delay(UnmeasurableWindowBackoff, cancellationToken);
        }

        status.Should().Be(HttpStatusCode.OK, payload);

        using var document = JsonDocument.Parse(payload);
        document.RootElement.ValueKind.Should().Be(
            JsonValueKind.Array, "the bounded stream endpoints answer with one JSON array");
        return [.. document.RootElement.EnumerateArray().Select(entry => entry.Clone())];
    }

    private static async Task<TimeSpan> AdvertisedEmissionDelayAsync(
        RestConformanceLane lane, CancellationToken cancellationToken)
    {
        using var wireClient = lane.CreateWireLevelClient();
        using var response = await wireClient.GetAsync(new Uri(VersionPath, UriKind.Relative), cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, payload);

        using var document = JsonDocument.Parse(payload);
        TryReadEmissionDelay(document.RootElement, out var advertised).Should().BeTrue(
            $"the probe's budget has to outlive a delay the participant states, and {VersionPath} answered {payload}");

        return advertised;
    }

    private static bool TryReadEmissionDelay(JsonElement version, out TimeSpan delay)
    {
        delay = default;
        if (!version.TryGetProperty(FeaturesName, out var features)
            || !TryGetEitherCase(
                features, OffsetCheckpointSnakeCase, OffsetCheckpointCamelCase, out var offsetCheckpoint)
            || !TryGetEitherCase(
                offsetCheckpoint, EmissionDelaySnakeCase, EmissionDelayCamelCase, out var advertised))
        {
            return false;
        }

        return TryReadProtobufDuration(advertised, out delay);
    }

    /// <summary>
    /// Reads a key the participant may answer with under either the proto snake_case name the
    /// vendored specification once declared or the camelCase name it sends today, so the probe's
    /// budget survives a participant on either naming.
    /// </summary>
    private static bool TryGetEitherCase(
        JsonElement root, string snakeCaseName, string camelCaseName, out JsonElement value) =>
        root.TryGetProperty(snakeCaseName, out value) || root.TryGetProperty(camelCaseName, out value);

    private static bool TryReadProtobufDuration(JsonElement duration, out TimeSpan parsed)
    {
        parsed = default;
        if (duration.ValueKind == JsonValueKind.Object)
        {
            if (!duration.TryGetProperty(DurationSecondsName, out var seconds)
                || !seconds.TryGetInt64(out var wholeSeconds))
            {
                return false;
            }

            var nanos = duration.TryGetProperty(DurationNanosName, out var nanoseconds)
                && nanoseconds.TryGetInt32(out var wholeNanos) ? wholeNanos : 0;
            parsed = TimeSpan.FromSeconds(wholeSeconds) + TimeSpan.FromTicks(nanos / NanosecondsPerTick);
            return true;
        }

        return duration.ValueKind == JsonValueKind.String
            && TryReadCanonicalDuration(duration.GetString(), out parsed);
    }

    private static bool TryReadCanonicalDuration(string? canonical, out TimeSpan parsed)
    {
        parsed = default;
        if (canonical is not { Length: > 1 }
            || !canonical.EndsWith('s')
            || !double.TryParse(canonical[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var elapsed))
        {
            return false;
        }

        parsed = TimeSpan.FromSeconds(elapsed);
        return true;
    }

    /// <summary>
    /// Whether the participant answered something that carries no evidence either way about offset
    /// checkpoints, and so is reopened rather than asserted on: the stale stream authorization a
    /// fresh party allocation provokes, read off the error code the client reads, and the request
    /// timeout a participant under load answers with before the window it was asked to hold has
    /// elapsed.
    /// </summary>
    private static async Task<bool> SaysNothingAboutCheckpointsAsync(
        HttpResponseMessage response, CancellationToken cancellationToken) =>
        response.StatusCode == HttpStatusCode.ServiceUnavailable
        || (response.StatusCode == HttpStatusCode.Conflict
            && (await RestErrorParser.ParseAsync(response, cancellationToken)).ReportedErrorId
                == StaleStreamAuthorization);

    private static bool IsOffsetCheckpoint(JsonElement entry) =>
        entry.TryGetProperty("update", out var update) && update.TryGetProperty("OffsetCheckpoint", out _);

    private static bool CarriesNoEvent(JsonElement entry) =>
        !entry.TryGetProperty("update", out var update)
        || (!update.TryGetProperty("Transaction", out _) && !update.TryGetProperty("Reassignment", out _));

    private sealed record WindowFollow(IReadOnlyList<JsonElement> Entries, int Windows, TimeSpan Elapsed);
}
