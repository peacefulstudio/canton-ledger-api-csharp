// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Grpc.Core;

namespace Canton.Ledger.Grpc.Client;

internal readonly record struct StreamMoveResult(bool Moved, RpcException? Faulted)
{
    public static async Task<StreamMoveResult> NextAsync<TResponse>(
        IAsyncStreamReader<TResponse> stream,
        CancellationToken cancellationToken)
    {
        try
        {
            var moved = await stream.MoveNext(cancellationToken);
            return new StreamMoveResult(moved, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RpcException ex)
        {
            return new StreamMoveResult(false, ex);
        }
    }
}
