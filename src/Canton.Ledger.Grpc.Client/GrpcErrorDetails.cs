// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;
using Google.Protobuf;
using Google.Rpc;
using GrpcStatus = Google.Rpc.Status;

namespace Canton.Ledger.Grpc.Client;

/// <summary>
/// Reads the <c>google.rpc.ErrorInfo</c> a participant packs into <c>google.rpc.Status.details</c>,
/// shared by the error parser (which reads it off a failed call's trailers) and the completion
/// projector (which reads it off a rejected completion's status). Every detail type other than
/// <c>ErrorInfo</c> is skipped, and a detail that does not decode reads as no error info at all,
/// so neither caller can be made to throw by what a participant put on the wire.
/// </summary>
internal static class GrpcErrorDetails
{
    private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
        FrozenDictionary<string, string>.Empty;

    public static ErrorInfo? FindErrorInfo(GrpcStatus? status)
    {
        if (status is null)
            return null;

        foreach (var detail in status.Details)
        {
            if (!detail.Is(ErrorInfo.Descriptor))
                continue;

            try
            {
                return detail.Unpack<ErrorInfo>();
            }
            catch (InvalidProtocolBufferException)
            {
                return null;
            }
        }

        return null;
    }

    public static IReadOnlyDictionary<string, string> ToMetadata(ErrorInfo? errorInfo)
    {
        if (errorInfo is null || errorInfo.Metadata.Count == 0)
            return EmptyMetadata;

        var metadata = new Dictionary<string, string>(errorInfo.Metadata.Count, StringComparer.Ordinal);
        foreach (var entry in errorInfo.Metadata)
        {
            metadata[entry.Key] = entry.Value;
        }

        return metadata;
    }
}
