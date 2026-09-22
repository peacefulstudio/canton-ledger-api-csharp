// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Rest.Client;
using Xunit;
using Any = Google.Protobuf.WellKnownTypes.Any;
using ByteString = Google.Protobuf.ByteString;
using Duration = Google.Protobuf.WellKnownTypes.Duration;
using ErrorInfo = Google.Rpc.ErrorInfo;
using ProtoCompletion = Com.Daml.Ledger.Api.V2.Completion;
using ProtoStatus = Google.Rpc.Status;
using ProtoSynchronizerTime = Com.Daml.Ledger.Api.V2.SynchronizerTime;
using ProtoTraceContext = Com.Daml.Ledger.Api.V2.TraceContext;
using RetryInfo = Google.Rpc.RetryInfo;
using Timestamp = Google.Protobuf.WellKnownTypes.Timestamp;
using WireCompletion = Canton.Ledger.Rest.Client.Raw.Completion;
using WireStatus = Canton.Ledger.Rest.Client.Raw.Status;
using WireSynchronizerTime = Canton.Ledger.Rest.Client.Raw.SynchronizerTime;
using WireTraceContext = Canton.Ledger.Rest.Client.Raw.TraceContext;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// Pins that one completion payload reaches a caller as the same neutral
/// <see cref="Completion"/> whichever transport carried it. Each row of
/// <see cref="Payloads"/> holds both wire encodings of a single completion — the protobuf
/// message and the JSON Ledger API DTO — and the body runs
/// <c>GrpcCompletionProjector.Project</c> and <c>RestCompletionProjector.Project</c> over
/// them together, so a row cannot exist for one transport and not the other and a
/// divergence fails here rather than only in that transport's own unit suite. Both
/// projectors are <c>internal</c> and stay that way; this project reaches them through
/// <c>InternalsVisibleTo</c> from each client.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="Payloads"/> rows cover the fidelity fields the neutral payload carries
/// alongside the verdict — <see cref="Completion.PaidTrafficCost"/> and
/// <see cref="Completion.TraceContext"/> — including the absent case on each, which is where
/// the two encodings differ most: <c>completion.proto</c> declares <c>paid_traffic_cost</c>
/// without presence so the protobuf message reads an absent cost back as zero, while the JSON
/// transport omits the property entirely, and the two have to land on one value.
/// </para>
/// <para>
/// The <see cref="Rejections"/> rows cover the structured rejection detail —
/// <see cref="CompletionStatus.ErrorId"/> and <see cref="CompletionStatus.Metadata"/>, decoded
/// from <c>google.rpc.Status.details</c>. That is where the two encodings diverge furthest: the
/// same detail arrives as a binary-packed <c>google.protobuf.Any</c> over gRPC and as a JSON
/// object tagged with <c>@type</c> over HTTP, so each row states the JSON arm as the body the
/// participant serves and deserializes it through the generated DTO rather than hand-building
/// one. Each row also pins that neither projector throws over a detail it cannot read.
/// </para>
/// </remarks>
public class LedgerCompletionProjectionParityTests
{
    private static readonly DateTimeOffset RecordTime =
        new(2026, 9, 20, 11, 0, 0, TimeSpan.Zero);

    private const string Traceparent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";
    private const string Tracestate = "peaceful=1";

    public static TheoryData<string> PayloadNames() => [.. Payloads.Keys];

    [Theory]
    [MemberData(nameof(PayloadNames))]
    public void Both_transports_project_one_completion_into_the_same_neutral_payload(string payloadName)
    {
        var payload = Payloads[payloadName];

        var overGrpc = GrpcCompletionProjector.Project(payload.OverGrpc());
        var overRest = RestCompletionProjector.Project(payload.OverRest());

        overRest.Should().Be(
            overGrpc,
            "one completion has to reach a caller as the same neutral payload on both transports");
    }

    [Theory]
    [MemberData(nameof(PayloadNames))]
    public void Both_transports_report_the_same_paid_traffic_cost(string payloadName)
    {
        var payload = Payloads[payloadName];

        CompletionOf(GrpcCompletionProjector.Project(payload.OverGrpc()))
            .PaidTrafficCost.Should().Be(payload.PaidTrafficCost);
        CompletionOf(RestCompletionProjector.Project(payload.OverRest()))
            .PaidTrafficCost.Should().Be(payload.PaidTrafficCost);
    }

    [Theory]
    [MemberData(nameof(PayloadNames))]
    public void Both_transports_report_the_same_trace_context(string payloadName)
    {
        var payload = Payloads[payloadName];

        CompletionOf(GrpcCompletionProjector.Project(payload.OverGrpc()))
            .TraceContext.Should().Be(payload.TraceContext);
        CompletionOf(RestCompletionProjector.Project(payload.OverRest()))
            .TraceContext.Should().Be(payload.TraceContext);
    }

    public static TheoryData<string> RejectionNames() => [.. Rejections.Keys];

    [Theory]
    [MemberData(nameof(RejectionNames))]
    public void Both_transports_project_one_rejection_into_the_same_neutral_payload(string rejectionName)
    {
        var rejection = Rejections[rejectionName];

        var overGrpc = GrpcCompletionProjector.Project(rejection.OverGrpc());
        var overRest = RestCompletionProjector.Project(rejection.OverRest());

        overRest.Should().Be(
            overGrpc,
            "one rejected completion has to reach a caller as the same neutral payload on both transports");
    }

    [Theory]
    [MemberData(nameof(RejectionNames))]
    public void Both_transports_report_the_same_rejection_error_id(string rejectionName)
    {
        var rejection = Rejections[rejectionName];

        StatusOf(GrpcCompletionProjector.Project(rejection.OverGrpc()))
            .ErrorId.Should().Be(rejection.ErrorId);
        StatusOf(RestCompletionProjector.Project(rejection.OverRest()))
            .ErrorId.Should().Be(rejection.ErrorId);
    }

    [Theory]
    [MemberData(nameof(RejectionNames))]
    public void Both_transports_report_the_same_rejection_metadata(string rejectionName)
    {
        var rejection = Rejections[rejectionName];

        StatusOf(GrpcCompletionProjector.Project(rejection.OverGrpc()))
            .Metadata.Should().BeEquivalentTo(rejection.Metadata);
        StatusOf(RestCompletionProjector.Project(rejection.OverRest()))
            .Metadata.Should().BeEquivalentTo(rejection.Metadata);
    }

    private static Completion CompletionOf(CompletionStreamEvent streamEvent) =>
        streamEvent.Should().BeOfType<CompletionStreamEvent.CommandAccepted>().Subject.Completion;

    private static CompletionStatus StatusOf(CompletionStreamEvent streamEvent) =>
        streamEvent.Should().BeOfType<CompletionStreamEvent.CommandRejected>().Subject.Status;

    private static readonly IReadOnlyDictionary<string, CompletionPayload> Payloads =
        new Dictionary<string, CompletionPayload>(StringComparer.Ordinal)
        {
            ["a completion carrying a trace context and a paid traffic cost"] = new(
                () => Proto("cmd-full", proto =>
                {
                    proto.PaidTrafficCost = 4096L;
                    proto.TraceContext = new ProtoTraceContext
                    {
                        Traceparent = Traceparent,
                        Tracestate = Tracestate,
                    };
                }),
                () => Wire("cmd-full", wire =>
                {
                    wire.PaidTrafficCost = "4096";
                    wire.TraceContext = new WireTraceContext
                    {
                        Traceparent = Traceparent,
                        Tracestate = Tracestate,
                    };
                }),
                PaidTrafficCost: 4096L,
                TraceContext: new TraceContext(Traceparent, Tracestate)),

            ["a completion carrying neither"] = new(
                () => Proto("cmd-bare", _ => { }),
                () => Wire("cmd-bare", _ => { }),
                PaidTrafficCost: 0L,
                TraceContext: null),

            ["a completion that reports an explicit zero traffic cost"] = new(
                () => Proto("cmd-zero", proto => proto.PaidTrafficCost = 0L),
                () => Wire("cmd-zero", wire => wire.PaidTrafficCost = "0"),
                PaidTrafficCost: 0L,
                TraceContext: null),

            ["a completion whose trace context names only the traceparent"] = new(
                () => Proto("cmd-traceparent", proto =>
                    proto.TraceContext = new ProtoTraceContext { Traceparent = Traceparent }),
                () => Wire("cmd-traceparent", wire =>
                    wire.TraceContext = new WireTraceContext { Traceparent = Traceparent }),
                PaidTrafficCost: 0L,
                TraceContext: new TraceContext(Traceparent, null)),

            ["a completion whose trace context names only the tracestate"] = new(
                () => Proto("cmd-tracestate", proto =>
                    proto.TraceContext = new ProtoTraceContext { Tracestate = Tracestate }),
                () => Wire("cmd-tracestate", wire =>
                    wire.TraceContext = new WireTraceContext { Tracestate = Tracestate }),
                PaidTrafficCost: 0L,
                TraceContext: null),

            ["a completion whose trace context names a blank traceparent and a tracestate"] = new(
                () => Proto("cmd-blank-traceparent", proto =>
                    proto.TraceContext = new ProtoTraceContext { Traceparent = "  ", Tracestate = Tracestate }),
                () => Wire("cmd-blank-traceparent", wire =>
                    wire.TraceContext = new WireTraceContext { Traceparent = "  ", Tracestate = Tracestate }),
                PaidTrafficCost: 0L,
                TraceContext: null),

            ["a completion whose trace context names an empty traceparent and a tracestate"] = new(
                () => Proto("cmd-empty-traceparent", proto =>
                    proto.TraceContext = new ProtoTraceContext { Traceparent = string.Empty, Tracestate = Tracestate }),
                () => Wire("cmd-empty-traceparent", wire =>
                    wire.TraceContext = new WireTraceContext { Traceparent = string.Empty, Tracestate = Tracestate }),
                PaidTrafficCost: 0L,
                TraceContext: null),

            ["a completion whose trace context is present but names nothing"] = new(
                () => Proto("cmd-empty-trace", proto => proto.TraceContext = new ProtoTraceContext()),
                () => Wire("cmd-empty-trace", wire => wire.TraceContext = new WireTraceContext()),
                PaidTrafficCost: 0L,
                TraceContext: null),
        };

    private static readonly IReadOnlyDictionary<string, string> NoMetadata =
        new Dictionary<string, string>(0, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, RejectedCompletion> Rejections =
        new Dictionary<string, RejectedCompletion>(StringComparer.Ordinal)
        {
            ["a rejection carrying no details"] = new(
                () => Proto("cmd-bare-rejection", proto => proto.Status = new ProtoStatus
                {
                    Code = 3,
                    Message = "the participant rejected the command",
                }),
                () => Wire("cmd-bare-rejection", wire => wire.Status = WireStatusFrom(
                    """
                    { "code": 3, "message": "the participant rejected the command" }
                    """)),
                ErrorId: null,
                Metadata: NoMetadata),

            ["a rejection whose ErrorInfo names a reason and metadata"] = new(
                () => Proto("cmd-not-found", proto => proto.Status = new ProtoStatus
                {
                    Code = 5,
                    Message = "the contract was not found",
                    Details =
                    {
                        Any.Pack(new ErrorInfo
                        {
                            Reason = "CONTRACT_NOT_FOUND",
                            Domain = "ledger-api",
                            Metadata = { { "category", "11" }, { "definite_answer", "false" } },
                        }),
                    },
                }),
                () => Wire("cmd-not-found", wire => wire.Status = WireStatusFrom(
                    """
                    {
                      "code": 5,
                      "message": "the contract was not found",
                      "details": [
                        {
                          "@type": "type.googleapis.com/google.rpc.ErrorInfo",
                          "reason": "CONTRACT_NOT_FOUND",
                          "domain": "ledger-api",
                          "metadata": { "category": "11", "definite_answer": "false" }
                        }
                      ]
                    }
                    """)),
                ErrorId: "CONTRACT_NOT_FOUND",
                Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["category"] = "11",
                    ["definite_answer"] = "false",
                }),

            ["a rejection whose details name a type the client does not read before the ErrorInfo"] = new(
                () => Proto("cmd-contention", proto => proto.Status = new ProtoStatus
                {
                    Code = 10,
                    Message = "the command lost a race",
                    Details =
                    {
                        Any.Pack(new RetryInfo { RetryDelay = Duration.FromTimeSpan(TimeSpan.FromSeconds(1)) }),
                        Any.Pack(new ErrorInfo
                        {
                            Reason = "CONTENTION_ON_SHARED_RESOURCES",
                            Metadata = { { "category", "10" } },
                        }),
                    },
                }),
                () => Wire("cmd-contention", wire => wire.Status = WireStatusFrom(
                    """
                    {
                      "code": 10,
                      "message": "the command lost a race",
                      "details": [
                        { "@type": "type.googleapis.com/google.rpc.RetryInfo", "retryDelay": "1s" },
                        {
                          "@type": "type.googleapis.com/google.rpc.ErrorInfo",
                          "reason": "CONTENTION_ON_SHARED_RESOURCES",
                          "metadata": { "category": "10" }
                        }
                      ]
                    }
                    """)),
                ErrorId: "CONTENTION_ON_SHARED_RESOURCES",
                Metadata: new Dictionary<string, string>(StringComparer.Ordinal) { ["category"] = "10" }),

            ["a rejection whose only detail is of a type the client does not read"] = new(
                () => Proto("cmd-retry-only", proto => proto.Status = new ProtoStatus
                {
                    Code = 14,
                    Message = "come back later",
                    Details = { Any.Pack(new RetryInfo { RetryDelay = Duration.FromTimeSpan(TimeSpan.FromSeconds(1)) }) },
                }),
                () => Wire("cmd-retry-only", wire => wire.Status = WireStatusFrom(
                    """
                    {
                      "code": 14,
                      "message": "come back later",
                      "details": [
                        { "@type": "type.googleapis.com/google.rpc.RetryInfo", "retryDelay": "1s" }
                      ]
                    }
                    """)),
                ErrorId: null,
                Metadata: NoMetadata),

            ["a rejection whose ErrorInfo detail cannot be decoded"] = new(
                () => Proto("cmd-undecodable", proto => proto.Status = new ProtoStatus
                {
                    Code = 13,
                    Message = "the participant said something unreadable",
                    Details =
                    {
                        new Any
                        {
                            TypeUrl = "type.googleapis.com/google.rpc.ErrorInfo",
                            Value = ByteString.CopyFrom([0xFF, 0xFF, 0xFF]),
                        },
                    },
                }),
                () => Wire("cmd-undecodable", wire => wire.Status = WireStatusFrom(
                    """
                    {
                      "code": 13,
                      "message": "the participant said something unreadable",
                      "details": [
                        { "@type": "type.googleapis.com/google.rpc.ErrorInfo", "metadata": "not an object" }
                      ]
                    }
                    """)),
                ErrorId: null,
                Metadata: NoMetadata),

            ["a rejection whose ErrorInfo names a reason and no metadata"] = new(
                () => Proto("cmd-unknown-party", proto => proto.Status = new ProtoStatus
                {
                    Code = 9,
                    Message = "the party is not known",
                    Details = { Any.Pack(new ErrorInfo { Reason = "PARTY_NOT_KNOWN_ON_LEDGER" }) },
                }),
                () => Wire("cmd-unknown-party", wire => wire.Status = WireStatusFrom(
                    """
                    {
                      "code": 9,
                      "message": "the party is not known",
                      "details": [
                        { "@type": "type.googleapis.com/google.rpc.ErrorInfo", "reason": "PARTY_NOT_KNOWN_ON_LEDGER" }
                      ]
                    }
                    """)),
                ErrorId: "PARTY_NOT_KNOWN_ON_LEDGER",
                Metadata: NoMetadata),

            ["a rejection whose ErrorInfo names an empty reason"] = new(
                () => Proto("cmd-blank-reason", proto => proto.Status = new ProtoStatus
                {
                    Code = 3,
                    Message = "the participant named no error id",
                    Details = { Any.Pack(new ErrorInfo { Reason = string.Empty, Metadata = { { "category", "8" } } }) },
                }),
                () => Wire("cmd-blank-reason", wire => wire.Status = WireStatusFrom(
                    """
                    {
                      "code": 3,
                      "message": "the participant named no error id",
                      "details": [
                        {
                          "@type": "type.googleapis.com/google.rpc.ErrorInfo",
                          "reason": "",
                          "metadata": { "category": "8" }
                        }
                      ]
                    }
                    """)),
                ErrorId: null,
                Metadata: new Dictionary<string, string>(StringComparer.Ordinal) { ["category"] = "8" }),
        };

    private static WireStatus WireStatusFrom(string servedBody) =>
        JsonSerializer.Deserialize<WireStatus>(servedBody)
        ?? throw new InvalidOperationException($"The served status body did not deserialize: {servedBody}");

    private static ProtoCompletion Proto(string commandId, Action<ProtoCompletion> refine)
    {
        var completion = new ProtoCompletion
        {
            CommandId = commandId,
            UpdateId = "update-1",
            Offset = 42L,
            UserId = "user-1",
            SynchronizerTime = new ProtoSynchronizerTime
            {
                SynchronizerId = "sync-1",
                RecordTime = Timestamp.FromDateTimeOffset(RecordTime),
            },
        };
        completion.ActAs.Add("alice");
        refine(completion);
        return completion;
    }

    private static WireCompletion Wire(string commandId, Action<WireCompletion> refine)
    {
        var completion = new WireCompletion
        {
            CommandId = commandId,
            UpdateId = "update-1",
            Offset = "42",
            UserId = "user-1",
            ActAs = ["alice"],
            SynchronizerTime = new WireSynchronizerTime
            {
                SynchronizerId = "sync-1",
                RecordTime = RecordTime,
            },
        };
        refine(completion);
        return completion;
    }

    private sealed record CompletionPayload(
        Func<ProtoCompletion> OverGrpc,
        Func<WireCompletion> OverRest,
        long PaidTrafficCost,
        TraceContext? TraceContext);

    private sealed record RejectedCompletion(
        Func<ProtoCompletion> OverGrpc,
        Func<WireCompletion> OverRest,
        string? ErrorId,
        IReadOnlyDictionary<string, string> Metadata);
}
