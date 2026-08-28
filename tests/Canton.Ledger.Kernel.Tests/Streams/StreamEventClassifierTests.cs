// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using AwesomeAssertions;
using Canton.Ledger.Kernel.Streams;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Canton.Ledger.Kernel.Tests.Streams;

public class StreamEventClassifierTests
{
    private const long EventOffset = 4711L;

    private static readonly SynchronizerId Synchronizer = new("sync::fingerprint::3");

    [Theory]
    [InlineData(UnclassifiedKind.CreatedEvent)]
    [InlineData(UnclassifiedKind.ArchivedEvent)]
    [InlineData(UnclassifiedKind.ExercisedEvent)]
    [InlineData(UnclassifiedKind.AssignedEvent)]
    [InlineData(UnclassifiedKind.UnassignedEvent)]
    public void TryAdmit_surfaces_the_wire_shapes_own_kind_when_the_marker_does_not_match(UnclassifiedKind unmatchedKind)
    {
        var decoded = new DecodedStreamEvent<SynchronizerId>(
            EventOffset, MatchesMarker: false, Synchronizer, unmatchedKind);

        var admitted = StreamEventClassifier.TryAdmit<SubscribedMarker, SynchronizerId>(
            decoded, out _, out var unclassified);

        admitted.Should().BeFalse();
        unclassified!.Kind.Should().Be(unmatchedKind);
        unclassified.Offset.Should().Be(LedgerOffset.At(EventOffset));
    }

    [Fact]
    public void TryAdmit_surfaces_MissingSynchronizerId_when_the_marker_matches_but_no_synchronizer_is_carried()
    {
        var decoded = new DecodedStreamEvent<SynchronizerId>(
            EventOffset, MatchesMarker: true, SynchronizerScope: null, UnclassifiedKind.CreatedEvent);

        var admitted = StreamEventClassifier.TryAdmit<SubscribedMarker, SynchronizerId>(
            decoded, out _, out var unclassified);

        admitted.Should().BeFalse();
        unclassified!.Kind.Should().Be(UnclassifiedKind.MissingSynchronizerId);
        unclassified.Offset.Should().Be(LedgerOffset.At(EventOffset));
    }

    [Fact]
    public void TryAdmit_reports_a_marker_mismatch_ahead_of_a_missing_synchronizer_id()
    {
        var decoded = new DecodedStreamEvent<SynchronizerId>(
            EventOffset, MatchesMarker: false, SynchronizerScope: null, UnclassifiedKind.ArchivedEvent);

        StreamEventClassifier.TryAdmit<SubscribedMarker, SynchronizerId>(decoded, out _, out var unclassified);

        unclassified!.Kind.Should().Be(UnclassifiedKind.ArchivedEvent);
    }

    [Fact]
    public void TryAdmit_hands_back_the_synchronizer_it_validated_so_no_caller_can_skip_the_rule()
    {
        var decoded = new DecodedStreamEvent<SynchronizerId>(
            EventOffset, MatchesMarker: true, Synchronizer, UnclassifiedKind.CreatedEvent);

        var admitted = StreamEventClassifier.TryAdmit<SubscribedMarker, SynchronizerId>(
            decoded, out var scope, out var unclassified);

        admitted.Should().BeTrue();
        unclassified.Should().BeNull();
        scope.Should().Be(Synchronizer);
    }

    [Fact]
    public void TryAdmit_applies_the_same_rule_to_a_reassignments_source_and_target_pair()
    {
        var scope = new ReassignmentScope(new SynchronizerId("source"), new SynchronizerId("target"));
        var decoded = new DecodedStreamEvent<ReassignmentScope>(
            EventOffset, MatchesMarker: true, scope, UnclassifiedKind.AssignedEvent);

        var admitted = StreamEventClassifier.TryAdmit<SubscribedMarker, ReassignmentScope>(
            decoded, out var validated, out _);

        admitted.Should().BeTrue();
        validated.Should().Be(scope);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Synchronizer_is_absent_when_the_participant_carried_no_usable_id(string? wireSynchronizerId)
    {
        StreamEventClassifier.Synchronizer(wireSynchronizerId).Should().BeNull();
    }

    [Fact]
    public void Synchronizer_wraps_a_populated_id()
    {
        StreamEventClassifier.Synchronizer("sync::fingerprint::3").Should().Be(Synchronizer);
    }

    [Theory]
    [InlineData(null, "target")]
    [InlineData("source", null)]
    [InlineData("", "target")]
    [InlineData("source", "   ")]
    public void ReassignmentSynchronizers_are_absent_when_either_end_is_missing(string? source, string? target)
    {
        StreamEventClassifier.ReassignmentSynchronizers(source, target).Should().BeNull();
    }

    [Fact]
    public void ReassignmentSynchronizers_carry_both_ends_when_the_participant_populated_both()
    {
        StreamEventClassifier.ReassignmentSynchronizers("source", "target")
            .Should().Be(new ReassignmentScope(new SynchronizerId("source"), new SynchronizerId("target")));
    }

    [Fact]
    public void DecodeFailure_surfaces_DecodeFailure_at_the_offset_of_the_event_that_failed()
    {
        var unclassified = StreamEventClassifier.DecodeFailure<SubscribedMarker>(
            EventOffset, logger: null, new InvalidOperationException("poison payload"));

        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        unclassified.Offset.Should().Be(LedgerOffset.At(EventOffset));
    }

    [Fact]
    public void DecodeFailure_warns_once_naming_the_offset_the_subscribed_type_and_the_cause()
    {
        using var loggerFactory = new CapturingLoggerFactory();
        var cause = new InvalidOperationException("poison payload");

        StreamEventClassifier.DecodeFailure<SubscribedMarker>(
            EventOffset, loggerFactory.CreateLogger("stream"), cause);

        var record = loggerFactory.Records.Should().ContainSingle().Subject;
        record.Level.Should().Be(LogLevel.Warning);
        record.Message.Should().Contain(EventOffset.ToString(CultureInfo.InvariantCulture))
            .And.Contain(nameof(SubscribedMarker))
            .And.Contain("decode-failure");
        record.Exception.Should().BeSameAs(cause);
    }

    [Theory]
    [InlineData(typeof(OperationCanceledException))]
    [InlineData(typeof(TaskCanceledException))]
    public void IsDecodeFailure_never_swallows_a_cancellation(Type cancellation)
    {
        StreamEventClassifier.IsDecodeFailure((Exception)Activator.CreateInstance(cancellation)!)
            .Should().BeFalse();
    }

    [Fact]
    public void IsDecodeFailure_covers_any_other_exception_so_no_event_is_dropped()
    {
        StreamEventClassifier.IsDecodeFailure(new FormatException()).Should().BeTrue();
    }

    private sealed record SubscribedMarker : IDamlType
    {
        public static DamlTypeDescriptor DamlTypeId =>
            throw new NotSupportedException(
                "SubscribedMarker is a degenerate test double: the classifier is told whether the marker matched, never asked.");
    }
}
