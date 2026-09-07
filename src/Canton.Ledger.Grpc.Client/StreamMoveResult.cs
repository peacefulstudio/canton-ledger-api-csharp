// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Canton.Ledger.Kernel.Streams;
using Grpc.Core;

namespace Canton.Ledger.Grpc.Client;

internal readonly record struct StreamMoveResult
{
    private static readonly StreamMoveResult AdvancedMove = new(true, null);
    private static readonly StreamMoveResult ExhaustedMove = new(false, null);

    private StreamMoveResult(bool moved, RpcException? fault)
    {
        Moved = moved;
        Fault = fault;
    }

    public bool Moved { get; }

    private RpcException? Fault { get; }

    public static async Task<StreamMoveResult> NextAsync<TResponse>(
        IAsyncStreamReader<TResponse> stream,
        CancellationToken cancellationToken)
    {
        try
        {
            return await stream.MoveNext(cancellationToken).ConfigureAwait(false)
                ? AdvancedMove
                : ExhaustedMove;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RpcException ex) when (CallerCancellation.Signals(ex, cancellationToken))
        {
            throw CallerCancellation.AsOperationCanceled(ex, cancellationToken);
        }
        catch (RpcException ex)
        {
            return new StreamMoveResult(false, ex);
        }
    }

    public StreamFault? RecordFault(Activity? activity)
    {
        if (Fault is not { } fault) return null;

        activity.RecordGrpcError(fault);
        var parsed = DamlErrorParser.Parse(fault);

        return StreamFault.FromTransport(
            (int)fault.StatusCode,
            string.IsNullOrEmpty(fault.Status.Detail) ? fault.Message : fault.Status.Detail,
            parsed.ClassifiedCategory,
            parsed.ReportedErrorId,
            fault);
    }
}
