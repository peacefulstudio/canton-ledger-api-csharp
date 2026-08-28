// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
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

    private static readonly IReadOnlyDictionary<StatusCode, DamlErrorCategory> RedactedSecurityCategories =
        new Dictionary<StatusCode, DamlErrorCategory>
        {
            [StatusCode.Unauthenticated] = DamlErrorCategory.AuthInterceptorInvalidAuthenticationCredentials,
            [StatusCode.PermissionDenied] = DamlErrorCategory.AuthorizationChecksFailed,
        };

    internal static ParsedLedgerError Parse(RpcException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var statusCode = (int)exception.StatusCode;

        var status = TryReadStatus(exception.Trailers);
        if (status is null)
        {
            return ParsedLedgerError.Untyped(exception.Status.Detail, statusCode);
        }

        var errorInfo = ExtractErrorInfo(status);
        if (errorInfo is null)
        {
            return WithoutErrorInfo(exception.StatusCode, status.Message, statusCode);
        }

        var metadata = new Dictionary<string, string>(errorInfo.Metadata.Count, StringComparer.Ordinal);
        foreach (var kvp in errorInfo.Metadata)
        {
            metadata[kvp.Key] = kvp.Value;
        }

        return new ParsedLedgerError(
            ParsedLedgerError.MapCategory(metadata.TryGetValue(CategoryMetadataKey, out var raw) ? raw : null),
            ErrorId: errorInfo.Reason ?? string.Empty,
            Message: status.Message ?? string.Empty,
            Metadata: metadata,
            StatusCode: statusCode);
    }

    private static ParsedLedgerError WithoutErrorInfo(
        StatusCode transportStatus, string? message, int statusCode)
    {
        var untyped = ParsedLedgerError.Untyped(message, statusCode);

        return RedactedSecurityCategories.TryGetValue(transportStatus, out var category)
            ? untyped with { Category = category }
            : untyped;
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
