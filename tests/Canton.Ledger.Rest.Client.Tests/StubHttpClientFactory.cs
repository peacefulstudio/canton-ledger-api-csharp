// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

internal sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory, IDisposable
{
    private HttpClient? _client;

    public HttpClient CreateClient(string name)
    {
        Assert.Equal(ServiceCollectionExtensions.HttpClientName, name);
        return _client ??= new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://localhost:7575") };
    }

    public void Dispose() => _client?.Dispose();
}
