// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Wire;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Outcomes;
using Microsoft.Extensions.Logging;

namespace Canton.Ledger.Rest.Client;

internal sealed partial class RestCallEnvelope(IHttpClientFactory httpClientFactory, ILogger logger)
{
    private const int UndecodableBodyStatusCode = (int)HttpStatusCode.InternalServerError;

    public async Task<TResult> SendAsync<TResponse, TResult>(
        RestCall call,
        Func<TResponse, TResult> project,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        var attempt = await AttemptAsync(call, project, timeout, cancellationToken).ConfigureAwait(false);
        return attempt switch
        {
            Attempt<TResult>.Ok ok => ok.Result,
            Attempt<TResult>.Failed failed => throw ToException(failed.Failure),
            _ => throw new InvalidOperationException($"Unhandled REST call attempt: {attempt.GetType().Name}"),
        };
    }

    public async Task<ExerciseOutcome<TProjection>> TrySendAsync<TResponse, TProjection>(
        RestCall call,
        Func<TResponse, ExerciseOutcome<TProjection>> project,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        var attempt = await AttemptAsync(call, project, timeout, cancellationToken).ConfigureAwait(false);
        return attempt switch
        {
            Attempt<ExerciseOutcome<TProjection>>.Ok ok => ok.Result,
            Attempt<ExerciseOutcome<TProjection>>.Failed failed => ToOutcome<TProjection>(failed.Failure),
            _ => throw new InvalidOperationException($"Unhandled REST call attempt: {attempt.GetType().Name}"),
        };
    }

    internal static LedgerOperationException ToException(ParsedLedgerError parsed) => parsed switch
    {
        ParsedLedgerError.Structured structured => new LedgerOperationException(
            structured.Message, structured.Category, structured.ErrorId, structured.Metadata),
        ParsedLedgerError.Unstructured unstructured => new LedgerOperationException(
            unstructured.Message, unstructured.StatusCode, unstructured.Category),
        _ => throw new InvalidOperationException($"Unhandled parsed ledger error: {parsed.GetType().Name}"),
    };

    internal static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw ToException(await RestErrorParser.ParseAsync(response, cancellationToken).ConfigureAwait(false));
    }

    internal HttpClient CreateClient() =>
        httpClientFactory.CreateClient(ServiceCollectionExtensions.HttpClientName);

    private async Task<Attempt<TResult>> AttemptAsync<TResponse, TResult>(
        RestCall call,
        Func<TResponse, TResult> project,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        var client = CreateClient();
        using var timeoutSource = CreateTimeoutSource(timeout, cancellationToken);
        var requestToken = timeoutSource?.Token ?? cancellationToken;

        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(call.Method, call.Path);
            if (call.Body is { } body)
            {
                request.Content = JsonContent.Create(body, options: RestRefitSettings.SerializerOptions);
            }
            response = await client.SendAsync(request, requestToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (IsTransportFailure(failure, cancellationToken))
        {
            return Failed<TResult>(ClassifyTransport(failure, DeadlineExceeded(timeout)));
        }

        using (response)
        {
            try
            {
                if (!response.IsSuccessStatusCode)
                {
                    return Failed<TResult>(new RestCallFailure.Rejected(
                        await RestErrorParser.ParseAsync(response, requestToken).ConfigureAwait(false)));
                }

                var body = await response.Content
                    .ReadFromJsonAsync<TResponse>(RestRefitSettings.SerializerOptions, requestToken)
                    .ConfigureAwait(false);

                return body is null
                    ? Failed<TResult>(new RestCallFailure.Undecodable(call.MissingBodyMessage, null))
                    : new Attempt<TResult>.Ok(project(body));
            }
            catch (Exception failure) when (IsResponseFailure(failure, cancellationToken))
            {
                return Failed<TResult>(ClassifyResponse(
                    failure, DeadlineExceededWhileReading(timeout), call.MalformedBodyMessagePrefix));
            }
        }
    }

    private static bool IsTransportFailure(Exception failure, CancellationToken callerToken) =>
        failure is HttpRequestException
        || (failure is OperationCanceledException && !callerToken.IsCancellationRequested);

    private static bool IsResponseFailure(Exception failure, CancellationToken callerToken) =>
        IsTransportFailure(failure, callerToken) || IsUndecodableBody(failure);

    private static bool IsUndecodableBody(Exception failure) =>
        failure is JsonException || MalformedResponse.IsWireDecodeFailure(failure);

    private static RestCallFailure ClassifyTransport(Exception failure, string deadlineExceeded) =>
        failure is HttpRequestException transportFailure
            ? new RestCallFailure.Transport(
                (int)HttpStatusCode.ServiceUnavailable, transportFailure.Message, transportFailure)
            : new RestCallFailure.Transport((int)HttpStatusCode.RequestTimeout, deadlineExceeded, failure);

    private RestCallFailure ClassifyResponse(
        Exception failure, string deadlineExceeded, string malformedBodyMessagePrefix)
    {
        if (IsUndecodableBody(failure))
        {
            LogUndecodableResponseBody(logger, failure);
            return new RestCallFailure.Undecodable($"{malformedBodyMessagePrefix}{failure.Message}", failure);
        }

        return ClassifyTransport(failure, deadlineExceeded);
    }

    private static LedgerOperationException ToException(RestCallFailure failure) => failure switch
    {
        RestCallFailure.Rejected rejected => ToException(rejected.Parsed),
        RestCallFailure.Transport transport =>
            new LedgerOperationException(
                transport.Message, transport.StatusCode, innerException: transport.Cause),
        RestCallFailure.Undecodable { Cause: { } cause } undecodable =>
            new LedgerOperationException(
                undecodable.Message, UndecodableBodyStatusCode, innerException: cause),
        RestCallFailure.Undecodable undecodable =>
            new LedgerOperationException(undecodable.Message, UndecodableBodyStatusCode),
        _ => throw new InvalidOperationException($"Unhandled REST call failure: {failure.GetType().Name}"),
    };

    private static ExerciseOutcome<T> ToOutcome<T>(RestCallFailure failure) => failure switch
    {
        RestCallFailure.Rejected { Parsed: ParsedLedgerError.Structured structured } =>
            new ExerciseOutcome<T>.DamlError(
                structured.Category, structured.ErrorId, structured.Message, structured.Metadata),
        RestCallFailure.Rejected { Parsed: ParsedLedgerError.Unstructured unstructured } =>
            new ExerciseOutcome<T>.InfraError(
                unstructured.StatusCode, unstructured.Message, unstructured.Category),
        RestCallFailure.Transport transport =>
            new ExerciseOutcome<T>.InfraError(
                transport.StatusCode, transport.Message, SourceException: transport.Cause),
        RestCallFailure.Undecodable undecodable =>
            new ExerciseOutcome<T>.InfraError(
                UndecodableBodyStatusCode, undecodable.Message, SourceException: undecodable.Cause),
        _ => throw new InvalidOperationException($"Unhandled REST call failure: {failure.GetType().Name}"),
    };

    private static Attempt<TResult> Failed<TResult>(RestCallFailure failure) =>
        new Attempt<TResult>.Failed(failure);

    private static string DeadlineExceeded(TimeSpan? timeout) =>
        $"Request exceeded the {DescribeDeadline(timeout)} deadline.";

    private static string DeadlineExceededWhileReading(TimeSpan? timeout) =>
        $"Request exceeded the {DescribeDeadline(timeout)} deadline while reading the response body.";

    private static string DescribeDeadline(TimeSpan? timeout) =>
        timeout is { } window ? window.ToString() : "HttpClient default";

    internal static CancellationTokenSource? CreateTimeoutSource(TimeSpan? timeout, CancellationToken cancellationToken)
    {
        if (timeout is not { } window)
            return null;

        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(window);
        return source;
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The participant answered successfully, but the response body could not be decoded — surfaced as a failed call")]
    private static partial void LogUndecodableResponseBody(ILogger logger, Exception exception);

    private abstract record Attempt<TResult>
    {
        private Attempt()
        {
        }

        internal sealed record Ok(TResult Result) : Attempt<TResult>;

        internal sealed record Failed(RestCallFailure Failure) : Attempt<TResult>;
    }
}
