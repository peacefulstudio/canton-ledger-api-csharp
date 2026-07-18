// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Globalization;
using Canton.Ledger.Kernel.Telemetry;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Emits an OpenTelemetry HTTP client span for every JSON Ledger API request, following the
/// kernel's <see cref="LedgerActivitySource"/> naming convention (ADR 0006/0010). Register the
/// source as <c>tracing.AddSource(LedgerActivitySource.NameFor&lt;RestActivityHandler&gt;())</c>.
/// Span names and tags follow the OpenTelemetry HTTP semantic conventions.
/// </summary>
public sealed class RestActivityHandler : DelegatingHandler
{
    internal const string HttpRequestMethod = "http.request.method";
    internal const string ServerAddress = SemanticConventions.ServerAddress;
    internal const string ServerPort = SemanticConventions.ServerPort;
    internal const string UrlFull = "url.full";
    internal const string HttpResponseStatusCode = "http.response.status_code";
    internal const string ErrorType = SemanticConventions.ErrorType;

    private static readonly ActivitySource Source = LedgerActivitySource.Create<RestActivityHandler>();

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var activity = Source.StartActivity(request.Method.Method, ActivityKind.Client);
        SetRequestTags(activity, request);

        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            RecordResponse(activity, response);
            return response;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.SetTag(ErrorType, exception.GetType().FullName);
            throw;
        }
    }

    private static void SetRequestTags(Activity? activity, HttpRequestMessage request)
    {
        if (activity is null || request.RequestUri is not { IsAbsoluteUri: true } uri) return;

        activity.SetTag(HttpRequestMethod, request.Method.Method);
        activity.SetTag(ServerAddress, uri.Host);
        activity.SetTag(ServerPort, uri.Port);
        activity.SetTag(UrlFull, uri.AbsoluteUri);
    }

    private static void RecordResponse(Activity? activity, HttpResponseMessage response)
    {
        if (activity is null) return;

        var statusCode = (int)response.StatusCode;
        activity.SetTag(HttpResponseStatusCode, statusCode);

        if (!response.IsSuccessStatusCode)
        {
            var statusCodeText = statusCode.ToString(CultureInfo.InvariantCulture);
            activity.SetStatus(ActivityStatusCode.Error, response.ReasonPhrase);
            activity.SetTag(ErrorType, statusCodeText);
        }
    }
}
