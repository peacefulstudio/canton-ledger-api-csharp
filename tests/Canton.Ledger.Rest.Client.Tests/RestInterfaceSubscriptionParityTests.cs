// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Net;
using System.Text;
using Canton.Ledger.Testing.Helpers;
using Daml.Ledger.Abstractions;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestInterfaceSubscriptionParityTests : InterfaceSubscriptionParityTests
{
    protected override Task<ILedgerStreamer> OpenInterfaceStreamerAsync()
    {
        ILedgerStreamer streamer = new RestLedgerClient(
            new ViewedParticipantHttpClientFactory(new ViewedParticipantHandler()));

        return Task.FromResult(streamer);
    }

    private sealed class ViewedParticipantHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://localhost:7575") };
    }
}

/// <summary>
/// The participant half of the interface-subscription parity lane: it serves the ledger end, and one
/// active-contract row and one transaction event, each carrying the participant-computed view of
/// <see cref="IViewedInterfaceMarker"/> that the parity suite expects the client to decode.
/// </summary>
internal sealed class ViewedParticipantHandler : HttpMessageHandler
{
    private const string LedgerEndPath = "/v2/state/ledger-end";
    private const string ActiveContractsPath = "/v2/state/active-contracts";
    private const string UpdatesPath = "/v2/updates";

    private static readonly string CreatedEventJson =
        $$$"""
        {
          "offset": "{{{InterfaceSubscriptionParityTests.CreatedOffset.Value}}}",
          "nodeId": 0,
          "contractId": "{{{InterfaceSubscriptionParityTests.ViewedContractId}}}",
          "templateId": {"packageId": "impl-pkg", "moduleName": "Token.Impl", "entityName": "Asset"},
          "createArgument": {"fields": []},
          "interfaceViews": [
            {
              "interfaceId": {"packageId": "viewed-pkg", "moduleName": "Token.Api", "entityName": "IViewedHolding"},
              "viewStatus": {"code": 0, "message": ""},
              "viewValue": {"fields": [{"label": "amount", "value": {"numeric": "{{{Amount}}}"}}]}
            }
          ],
          "witnessParties": ["{{{InterfaceSubscriptionParityTests.Owner.Id}}}"]
        }
        """;

    private static string Amount =>
        InterfaceSubscriptionParityTests.ViewAmount.ToString(CultureInfo.InvariantCulture);

    private static string Synchronizer => InterfaceSubscriptionParityTests.Synchronizer.Id;

    private static readonly string LedgerEndJson =
        $$"""{"offset": {{InterfaceSubscriptionParityTests.Window.To.Value}}}""";

    private static readonly string ActiveContractsJson =
        $$"""
        [
          {
            "contractEntry": {
              "JsActiveContract": {
                "createdEvent": {{CreatedEventJson}},
                "synchronizerId": "{{Synchronizer}}",
                "reassignmentCounter": "0"
              }
            }
          }
        ]
        """;

    private static readonly string UpdatesJson =
        $$"""
        [
          {
            "update": {
              "Transaction": {
                "value": {
                  "updateId": "u-viewed",
                  "offset": "{{InterfaceSubscriptionParityTests.CreatedOffset.Value}}",
                  "synchronizerId": "{{Synchronizer}}",
                  "events": [{"CreatedEvent": {{CreatedEventJson}}}]
                }
              }
            }
          }
        ]
        """;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.RequestUri!.AbsolutePath switch
        {
            LedgerEndPath => LedgerEndJson,
            ActiveContractsPath => ActiveContractsJson,
            UpdatesPath => UpdatesJson,
            var unexpected => throw new InvalidOperationException(
                $"The interface-subscription lane seeds no response for '{unexpected}'."),
        };

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
    }
}
