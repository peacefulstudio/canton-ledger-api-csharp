// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Grpc.Core;

namespace Canton.Ledger.Grpc.Client;

/// <summary>
/// Classifies transport-level cancellation: Grpc.Net.Client defaults
/// <c>GrpcChannelOptions.ThrowOperationCanceledOnCancellation</c> to <c>false</c>, so a caller's
/// cancelled token surfaces as <see cref="RpcException"/> with <see cref="StatusCode.Cancelled"/>
/// rather than <see cref="OperationCanceledException"/>.
/// The transport does not say who cancelled, so a server-initiated <c>Cancelled</c> that races
/// with the caller's own cancellation is deliberately classified as caller cancellation — the
/// caller no longer wants the stream, and the server's status survives as the
/// <see cref="Exception.InnerException"/>.
/// </summary>
internal static class CallerCancellation
{
    public static bool Signals(RpcException exception, CancellationToken cancellationToken) =>
        exception.StatusCode == StatusCode.Cancelled && cancellationToken.IsCancellationRequested;

    public static OperationCanceledException AsOperationCanceled(
        RpcException exception,
        CancellationToken cancellationToken) =>
        new(exception.Status.Detail, exception, cancellationToken);
}
