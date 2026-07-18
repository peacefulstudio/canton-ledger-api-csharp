// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Refit;

namespace Canton.Ledger.Rest.Client.Tests;

internal static class RestApiFactory
{
    internal static (TApi Api, RecordingHttpHandler Transport) Build<TApi>() where TApi : class
    {
        var transport = new RecordingHttpHandler();
        var client = new HttpClient(transport) { BaseAddress = new Uri("http://localhost:7575") };
        return (RestService.For<TApi>(client, RestRefitSettings.Create()), transport);
    }
}
