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
            return UnknownError(exception.Status.Detail);
        }

        var errorInfo = ExtractErrorInfo(status);
        if (errorInfo is null)
        {
            return UnknownError(status.Message);
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

    private static (
        DamlErrorCategory Category,
        string ErrorId,
        string Message,
        IReadOnlyDictionary<string, string> Metadata)
        UnknownError(string? message) =>
        (DamlErrorCategory.Unknown, ErrorId: string.Empty,
            Message: message ?? string.Empty,
            Metadata: new Dictionary<string, string>(0));

    internal static DamlErrorCategory MapCategory(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return DamlErrorCategory.Unknown;

        return Enum.TryParse<DamlErrorCategory>(raw, ignoreCase: true, out var category) && Enum.IsDefined(category)
            ? category
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
