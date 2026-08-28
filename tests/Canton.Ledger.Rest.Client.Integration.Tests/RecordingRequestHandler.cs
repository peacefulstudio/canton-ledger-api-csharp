// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;

namespace Canton.Ledger.Rest.Client.Integration.Tests;

internal sealed class RecordingRequestHandler : DelegatingHandler
{
    public ConcurrentQueue<string> Bodies { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is not null)
        {
            Bodies.Enqueue(await request.Content.ReadAsStringAsync(cancellationToken));
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
