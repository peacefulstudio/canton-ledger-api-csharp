// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Daml.Runtime.Outcomes;
using Google.Protobuf;
using Google.Rpc;
using Grpc.Core;
using GrpcStatus = Google.Rpc.Status;

namespace Canton.Ledger.Grpc.Client;

/// <summary>
/// Decodes structured Canton/Daml error information from a gRPC <see cref="RpcException"/>.
///
/// Canton attaches a <see cref="GrpcStatus"/> in the <c>grpc-status-details-bin</c> trailer
/// (rich error model). Inside <c>Status.details</c>, the <see cref="ErrorInfo"/> entry carries:
/// <list type="bullet">
///   <item><c>reason</c> — the error ID (e.g. <c>CONTRACT_NOT_FOUND</c>, <c>MURMURES_SWAP_ALREADY_EXECUTED</c>);</item>
///   <item><c>metadata["category"]</c> — the Canton error category name; mapped to <see cref="DamlErrorCategory"/>;</item>
///   <item><c>metadata</c> — additional structured detail (cookies, definite_answer, etc.).</item>
/// </list>
///
/// When trailers are missing, the trailer is malformed, or no <see cref="ErrorInfo"/> entry
/// is present, falls back to <see cref="DamlErrorCategory.Unknown"/> and surfaces the raw
/// <see cref="RpcException.Status"/> message as <c>Message</c> with empty metadata —
/// information is degraded but never silently dropped.
/// </summary>
internal static class DamlErrorParser
{
    private const string GrpcStatusDetailsBinKey = "grpc-status-details-bin";
    private const string CategoryMetadataKey = "category";

    /// <summary>
    /// Builds an <see cref="ExerciseOutcome{T}.DamlError"/> from a gRPC failure.
    /// </summary>
    public static ExerciseOutcome<T>.DamlError ToDamlError<T>(RpcException exception)
    {
        var (category, errorId, message, metadata) = Parse(exception);
        return new ExerciseOutcome<T>.DamlError(category, errorId, message, metadata);
    }

    /// <summary>
    /// Parses an <see cref="RpcException"/> trailer into structured Canton/Daml error
    /// fields. Exposed for testing.
    /// </summary>
    internal static (
        DamlErrorCategory Category,
        string ErrorId,
        string Message,
        IReadOnlyDictionary<string, string> Metadata)
        Parse(RpcException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var status = TryReadStatus(exception.Trailers);
        if (status is null)
        {
            return (DamlErrorCategory.Unknown, ErrorId: string.Empty,
                Message: exception.Status.Detail ?? string.Empty,
                Metadata: new Dictionary<string, string>(0));
        }

        var errorInfo = ExtractErrorInfo(status);
        if (errorInfo is null)
        {
            // Status was present but carried no ErrorInfo; surface the status message.
            return (DamlErrorCategory.Unknown, ErrorId: string.Empty,
                Message: status.Message ?? string.Empty,
                Metadata: new Dictionary<string, string>(0));
        }

        var metadata = new Dictionary<string, string>(errorInfo.Metadata.Count, StringComparer.Ordinal);
        foreach (var kvp in errorInfo.Metadata)
        {
            metadata[kvp.Key] = kvp.Value;
        }

        var category = MapCategory(metadata.TryGetValue(CategoryMetadataKey, out var raw) ? raw : null);

        return (
            category,
            ErrorId: errorInfo.Reason ?? string.Empty,
            Message: status.Message ?? string.Empty,
            Metadata: metadata);
    }

    /// <summary>
    /// Maps the raw <c>ErrorInfo.metadata["category"]</c> value to the enum.
    /// Returns <see cref="DamlErrorCategory.Unknown"/> for null, empty, or unrecognised values.
    /// </summary>
    internal static DamlErrorCategory MapCategory(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return DamlErrorCategory.Unknown;

        // Canton emits PascalCase names (e.g. "TransientServerFailure"). Match exactly first,
        // then fall through to a case-insensitive match.
        if (Enum.TryParse<DamlErrorCategory>(raw, ignoreCase: false, out var exact))
            return exact;

        return Enum.TryParse<DamlErrorCategory>(raw, ignoreCase: true, out var loose)
            ? loose
            : DamlErrorCategory.Unknown;
    }

    private static GrpcStatus? TryReadStatus(Metadata? trailers)
    {
        if (trailers is null)
            return null;

        var entry = trailers.FirstOrDefault(t =>
            string.Equals(t.Key, GrpcStatusDetailsBinKey, StringComparison.OrdinalIgnoreCase));
        if (entry is null || !entry.IsBinary)
            return null;

        try
        {
            return GrpcStatus.Parser.ParseFrom(entry.ValueBytes);
        }
        catch (InvalidProtocolBufferException)
        {
            return null;
        }
    }

    private static ErrorInfo? ExtractErrorInfo(GrpcStatus status)
    {
        foreach (var detail in status.Details)
        {
            if (detail.Is(ErrorInfo.Descriptor))
            {
                try
                {
                    return detail.Unpack<ErrorInfo>();
                }
                catch (InvalidProtocolBufferException)
                {
                    return null;
                }
            }
        }

        return null;
    }
}
