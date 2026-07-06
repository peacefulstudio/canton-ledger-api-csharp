// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Outcomes;
using Google.Protobuf;
using Google.Rpc;
using Grpc.Core;
using GrpcStatus = Google.Rpc.Status;

namespace Canton.Ledger.Grpc.Client;

internal static class DamlErrorParser
{
    private const string GrpcStatusDetailsBinKey = "grpc-status-details-bin";
    private const string CategoryMetadataKey = "category";

    public static ExerciseOutcome<T>.DamlError ToDamlError<T>(RpcException exception)
    {
        var (category, errorId, message, metadata) = Parse(exception);
        return new ExerciseOutcome<T>.DamlError(category, errorId, message, metadata);
    }

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
