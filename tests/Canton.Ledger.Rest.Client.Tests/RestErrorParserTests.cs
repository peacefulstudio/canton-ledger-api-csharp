// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;
using AwesomeAssertions;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public class RestErrorParserTests
{
    private static HttpResponseMessage Response(HttpStatusCode statusCode, string? body) =>
        new(statusCode)
        {
            Content = body is null ? null! : new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage PlainTextResponse(HttpStatusCode statusCode, string body) =>
        new(statusCode) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };

    [Fact]
    public async Task ParseAsync_extracts_category_error_id_message_and_metadata_from_an_ErrorInfo_detail()
    {
        using var response = Response(
            HttpStatusCode.Conflict,
            """
            {
              "code": 9,
              "message": "DUPLICATE_COMMAND(9,abcd1234): A command with the given command id has already been submitted",
              "details": [
                {
                  "@type": "type.googleapis.com/google.rpc.ErrorInfo",
                  "reason": "DUPLICATE_COMMAND",
                  "domain": "com.daml.error",
                  "metadata": {"category": "ContentionOnSharedResources", "completion_offset": "123"}
                }
              ]
            }
            """);

        var parsed = await RestErrorParser.ParseAsync(response, TestContext.Current.CancellationToken);

        parsed.Category.Should().Be(DamlErrorCategory.ContentionOnSharedResources);
        parsed.ErrorId.Should().Be("DUPLICATE_COMMAND");
        parsed.Message.Should().StartWith("DUPLICATE_COMMAND(9,abcd1234)");
        parsed.Metadata.Should().Contain("completion_offset", "123");
        parsed.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ParseAsync_falls_back_to_Unknown_category_when_no_ErrorInfo_detail_is_present()
    {
        using var response = Response(
            HttpStatusCode.InternalServerError,
            """{"code": 2, "message": "internal error", "details": []}""");

        var parsed = await RestErrorParser.ParseAsync(response, TestContext.Current.CancellationToken);

        parsed.Category.Should().Be(DamlErrorCategory.Unknown);
        parsed.ErrorId.Should().BeEmpty();
        parsed.Message.Should().Be("internal error");
        parsed.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ParseAsync_falls_back_to_the_http_status_code_and_reason_when_the_body_is_not_json()
    {
        using var response = Response(HttpStatusCode.ServiceUnavailable, "not json");
        response.ReasonPhrase = "Service Unavailable";

        var parsed = await RestErrorParser.ParseAsync(response, TestContext.Current.CancellationToken);

        parsed.Category.Should().Be(DamlErrorCategory.Unknown);
        parsed.Message.Should().Be("not json");
        parsed.StatusCode.Should().Be((int)HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task ParseAsync_falls_back_to_the_http_status_code_when_the_body_is_empty()
    {
        using var response = Response(HttpStatusCode.NotFound, string.Empty);
        response.ReasonPhrase = "Not Found";

        var parsed = await RestErrorParser.ParseAsync(response, TestContext.Current.CancellationToken);

        parsed.Category.Should().Be(DamlErrorCategory.Unknown);
        parsed.Message.Should().Be("Not Found");
        parsed.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ParseAsync_does_not_match_a_detail_type_that_merely_contains_ErrorInfo_as_a_substring()
    {
        using var response = Response(
            HttpStatusCode.Conflict,
            """
            {
              "code": 9,
              "message": "some error",
              "details": [
                {
                  "@type": "type.googleapis.com/com.acme.MyCustomErrorInfoExt",
                  "reason": "SHOULD_NOT_MATCH",
                  "metadata": {"category": "ContentionOnSharedResources"}
                }
              ]
            }
            """);

        var parsed = await RestErrorParser.ParseAsync(response, TestContext.Current.CancellationToken);

        parsed.Category.Should().Be(DamlErrorCategory.Unknown);
        parsed.ErrorId.Should().BeEmpty();
    }

    [Theory]
    [InlineData("8", DamlErrorCategory.InvalidIndependentOfSystemState)]
    [InlineData("11", DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing)]
    [InlineData("ContentionOnSharedResources", DamlErrorCategory.ContentionOnSharedResources)]
    [InlineData("50", DamlErrorCategory.Unknown)]
    [InlineData("-1", DamlErrorCategory.Unknown)]
    [InlineData("TotallyMadeUpCategory", DamlErrorCategory.Unknown)]
    [InlineData("TransientServerFailure,ContentionOnSharedResources", DamlErrorCategory.Unknown)]
    public async Task ParseAsync_classifies_the_wire_category_identically_to_the_gRPC_transport(
        string wireCategory, DamlErrorCategory expected)
    {
        using var response = Response(
            HttpStatusCode.BadRequest,
            $$"""
            {
              "code": 3,
              "message": "SOMETHING(1,abcd1234): boom",
              "details": [
                {
                  "@type": "type.googleapis.com/google.rpc.ErrorInfo",
                  "reason": "SOMETHING",
                  "metadata": {"category": "{{wireCategory}}"}
                }
              ]
            }
            """);

        var parsed = await RestErrorParser.ParseAsync(response, TestContext.Current.CancellationToken);

        parsed.Category.Should().Be(expected);
    }

    [Fact]
    public async Task ParseAsync_maps_the_numeric_category_id_a_participant_actually_sends()
    {
        using var response = Response(
            HttpStatusCode.BadRequest,
            """
            {
              "code": 3,
              "message": "PROTO_DESERIALIZATION_FAILURE(8,0): Deserialization of protobuf message failed",
              "details": [
                {
                  "@type": "type.googleapis.com/google.rpc.ErrorInfo",
                  "reason": "PROTO_DESERIALIZATION_FAILURE",
                  "metadata": {"category": "8"}
                }
              ]
            }
            """);

        var parsed = await RestErrorParser.ParseAsync(response, TestContext.Current.CancellationToken);

        parsed.Category.Should().Be(DamlErrorCategory.InvalidIndependentOfSystemState);
        parsed.ErrorId.Should().Be("PROTO_DESERIALIZATION_FAILURE");
        parsed.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ParseAsync_decodes_the_JsCantonError_envelope_a_participant_serves()
    {
        using var response = Response(
            HttpStatusCode.NotFound,
            """
            {
              "code": "NOT_FOUND",
              "cause": "getting user failed for unknown user \"probe\"",
              "context": {"participant": "a-validator-1", "definite_answer": "false", "category": "11"},
              "errorCategory": 11
            }
            """);

        var parsed = await RestErrorParser.ParseAsync(response, TestContext.Current.CancellationToken);

        parsed.ErrorId.Should().Be("NOT_FOUND");
        parsed.Category.Should().Be(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing);
        parsed.Message.Should().Be("getting user failed for unknown user \"probe\"");
        parsed.Metadata.Should().Contain("category", "11");
        parsed.Metadata.Should().Contain("participant", "a-validator-1");
        parsed.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ParseAsync_decodes_a_JsCantonError_carrying_every_field_the_envelope_declares()
    {
        using var response = Response(
            HttpStatusCode.BadRequest,
            """
            {
              "code": "INVALID_ARGUMENT",
              "cause": "source and target synchronizers are the same",
              "correlationId": null,
              "traceId": "929179d2",
              "context": {"tid": "929179d2", "category": "8"},
              "resources": [["ErrorResource(USER)", "probe"]],
              "errorCategory": 8,
              "grpcCodeValue": 3,
              "retryInfo": null,
              "definiteAnswer": null
            }
            """);

        var parsed = await RestErrorParser.ParseAsync(response, TestContext.Current.CancellationToken);

        parsed.ErrorId.Should().Be("INVALID_ARGUMENT");
        parsed.Category.Should().Be(DamlErrorCategory.InvalidIndependentOfSystemState);
        parsed.Message.Should().Be("source and target synchronizers are the same");
        parsed.Metadata.Should().Contain("tid", "929179d2");
        parsed.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ParseAsync_classifies_a_JsCantonError_from_its_context_category_when_errorCategory_is_absent()
    {
        using var response = Response(
            HttpStatusCode.Conflict,
            """
            {
              "code": "DUPLICATE_COMMAND",
              "cause": "a command with the given command id has already been submitted",
              "context": {"category": "ContentionOnSharedResources"}
            }
            """);

        var parsed = await RestErrorParser.ParseAsync(response, TestContext.Current.CancellationToken);

        parsed.ErrorId.Should().Be("DUPLICATE_COMMAND");
        parsed.Category.Should().Be(DamlErrorCategory.ContentionOnSharedResources);
    }

    [Fact]
    public async Task ParseAsync_keeps_the_error_id_of_a_JsCantonError_whose_category_is_unrecognised()
    {
        using var response = Response(
            HttpStatusCode.InternalServerError,
            """{"code": "SOMETHING_NEW", "cause": "boom", "context": {}, "errorCategory": 50}""");

        var parsed = await RestErrorParser.ParseAsync(response, TestContext.Current.CancellationToken);

        parsed.ErrorId.Should().Be("SOMETHING_NEW");
        parsed.Category.Should().Be(DamlErrorCategory.Unknown);
        parsed.Message.Should().Be("boom");
    }

    [Theory]
    [InlineData("Invalid value")]
    [InlineData("Invalid value for: body")]
    [InlineData("Invalid value for: query parameter limit")]
    [InlineData("Invalid value for: body, Invalid value for: query parameter stream_idle_timeout_ms")]
    public async Task ParseAsync_classifies_a_text_plain_bad_request_as_an_invalid_request(string body)
    {
        using var response = PlainTextResponse(HttpStatusCode.BadRequest, body);

        var parsed = await RestErrorParser.ParseAsync(response, TestContext.Current.CancellationToken);

        parsed.Category.Should().Be(DamlErrorCategory.InvalidIndependentOfSystemState);
        parsed.Message.Should().Be(body);
        parsed.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ParseAsync_leaves_a_text_plain_bad_request_without_an_error_id_or_metadata()
    {
        using var response = PlainTextResponse(HttpStatusCode.BadRequest, "Invalid value for: body");

        var parsed = await RestErrorParser.ParseAsync(response, TestContext.Current.CancellationToken);

        parsed.ErrorId.Should().BeEmpty();
        parsed.Metadata.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseAsync_keeps_the_reason_phrase_when_a_text_plain_bad_request_carries_no_body()
    {
        using var response = PlainTextResponse(HttpStatusCode.BadRequest, string.Empty);
        response.ReasonPhrase = "Bad Request";

        var parsed = await RestErrorParser.ParseAsync(response, TestContext.Current.CancellationToken);

        parsed.Category.Should().Be(DamlErrorCategory.InvalidIndependentOfSystemState);
        parsed.Message.Should().Be("Bad Request");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ParseAsync_leaves_every_other_text_plain_status_unclassified(HttpStatusCode statusCode)
    {
        using var response = PlainTextResponse(statusCode, "something the participant wrote in prose");

        var parsed = await RestErrorParser.ParseAsync(response, TestContext.Current.CancellationToken);

        parsed.Category.Should().Be(DamlErrorCategory.Unknown);
        parsed.Message.Should().Be("something the participant wrote in prose");
        parsed.StatusCode.Should().Be((int)statusCode);
    }

    [Fact]
    public async Task ParseAsync_prefers_the_json_envelope_over_the_status_code_on_a_bad_request()
    {
        using var response = Response(
            HttpStatusCode.BadRequest,
            """
            {
              "code": "SOMETHING_NEW",
              "cause": "boom",
              "context": {},
              "errorCategory": 50
            }
            """);

        var parsed = await RestErrorParser.ParseAsync(response, TestContext.Current.CancellationToken);

        parsed.ErrorId.Should().Be("SOMETHING_NEW");
        parsed.Category.Should().Be(DamlErrorCategory.Unknown);
    }

    [Fact]
    public async Task ParseAsync_matches_the_text_plain_media_type_ignoring_case_and_charset()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("Invalid value for: body", Encoding.UTF8, "TEXT/PLAIN"),
        };

        var parsed = await RestErrorParser.ParseAsync(response, TestContext.Current.CancellationToken);

        parsed.Category.Should().Be(DamlErrorCategory.InvalidIndependentOfSystemState);
    }
}
