// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace Canton.Ledger.Rest.Client.Integration.Tests;

internal sealed partial class ServedOpenApiDocument
{
    private const string ServedPath = "/docs/openapi";

    internal static async Task<ServedOpenApiDocument> FetchAsync(
        HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(ServedPath, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            Assert.Fail(UnreadableMessage(client.BaseAddress, (int)response.StatusCode, body));
        }

        return Parse(body);
    }

    private static string UnreadableMessage(Uri? baseAddress, int statusCode, string body) =>
        $"GET {baseAddress}{ServedPath.TrimStart('/')} answered {statusCode}, so the participant's own "
        + "JSON Ledger API schema could not be read and the wire-shape claims this suite pins are neither "
        + "confirmed nor refuted. That endpoint is served by the participant itself and needs no "
        + "authentication, so a non-200 means the endpoint moved or this lane is not pointed at a Canton "
        + $"participant — it does not mean the served shape changed. Response body: {body}";
}
