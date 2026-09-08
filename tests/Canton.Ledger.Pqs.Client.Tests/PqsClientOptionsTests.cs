// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Pqs.Client.Tests;

public class PqsClientOptionsTests
{
    [Fact]
    public void JsonSerializerOptions_defaults_to_null()
    {
        var options = new PqsClientOptions { ConnectionString = "Host=localhost" };

        options.JsonSerializerOptions.Should().BeNull();
    }

    [Fact]
    public void JsonSerializerOptions_can_be_set()
    {
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        var options = new PqsClientOptions
        {
            ConnectionString = "Host=localhost",
            JsonSerializerOptions = jsonOptions
        };

        options.JsonSerializerOptions.Should().BeSameAs(jsonOptions);
    }

    [Fact]
    public void ConnectionString_is_required_via_data_annotations()
    {
        var options = new PqsClientOptions { ConnectionString = null! };
        var results = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(
            options,
            new ValidationContext(options),
            results,
            validateAllProperties: true);

        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(PqsClientOptions.ConnectionString)));
    }

    [Fact]
    public void ConnectionString_rejects_empty_string_via_data_annotations()
    {
        var options = new PqsClientOptions { ConnectionString = "" };
        var results = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(
            options,
            new ValidationContext(options),
            results,
            validateAllProperties: true);

        isValid.Should().BeFalse();
    }

    [Fact]
    public void CreateDefaultJsonSerializerOptions_returns_a_mutable_instance()
    {
        var jsonOptions = PqsClientOptions.CreateDefaultJsonSerializerOptions();

        jsonOptions.IsReadOnly.Should().BeFalse();
        jsonOptions.Converters.Add(new OriginConverterFactory());
    }

    [Fact]
    public void CreateDefaultJsonSerializerOptions_returns_a_fresh_instance_per_call()
    {
        var first = PqsClientOptions.CreateDefaultJsonSerializerOptions();
        var second = PqsClientOptions.CreateDefaultJsonSerializerOptions();

        first.Should().NotBeSameAs(second);
    }

    [Fact]
    public void CreateDefaultJsonSerializerOptions_does_not_leak_an_added_converter_into_the_next_call()
    {
        var augmented = PqsClientOptions.CreateDefaultJsonSerializerOptions();
        augmented.Converters.Add(new OriginConverterFactory());

        var pristine = PqsClientOptions.CreateDefaultJsonSerializerOptions();

        pristine.Converters.Should().NotContain(c => c is OriginConverterFactory);
    }

    [Fact]
    public void CreateDefaultJsonSerializerOptions_does_not_mutate_the_options_the_client_falls_back_to()
    {
        var augmented = PqsClientOptions.CreateDefaultJsonSerializerOptions();
        augmented.Converters.Add(new OriginConverterFactory());

        PqsClient.DefaultJsonSerializerOptions.Converters
            .Should().NotContain(c => c is OriginConverterFactory);
    }

    [Fact]
    public void CreateDefaultJsonSerializerOptions_carries_the_defaults_the_client_applies()
    {
        var jsonOptions = PqsClientOptions.CreateDefaultJsonSerializerOptions();

        jsonOptions.PropertyNameCaseInsensitive.Should().BeTrue();
        jsonOptions.NumberHandling.Should().Be(JsonNumberHandling.AllowReadingFromString);
        jsonOptions.Converters.Should().ContainSingle(c => c is JsonStringEnumConverter);
    }

    [Fact]
    public void CreateDefaultJsonSerializerOptions_augmented_with_variant_factory_deserializes_abstract_member_and_keeps_defaults()
    {
        var jsonOptions = PqsClientOptions.CreateDefaultJsonSerializerOptions();
        jsonOptions.Converters.Add(new OriginConverterFactory());
        var options = new PqsClientOptions
        {
            ConnectionString = "Host=localhost;Database=pqs",
            JsonSerializerOptions = jsonOptions
        };

        const string payloadJson = """{"price":"1.5","side":"Sell","origin":{"kind":"direct"}}""";
        var payload = JsonSerializer.Deserialize<OrderPayload>(payloadJson, options.JsonSerializerOptions);

        payload.Should().NotBeNull();
        payload!.Price.Should().Be(1.5m);
        payload.Side.Should().Be(Side.Sell);
        payload.Origin.Should().BeOfType<DirectOrigin>();
    }

    [Fact]
    public void CreateDefaultJsonSerializerOptions_without_augmentation_throws_on_abstract_member()
    {
        const string payloadJson = """{"price":"1.5","side":"Sell","origin":{"kind":"direct"}}""";

        var act = () => JsonSerializer.Deserialize<OrderPayload>(
            payloadJson, PqsClientOptions.CreateDefaultJsonSerializerOptions());

        act.Should().Throw<NotSupportedException>();
    }

    private sealed record OrderPayload(decimal Price, Side Side, ConnectionOrigin Origin);

    private enum Side { Buy, Sell }

    private abstract record ConnectionOrigin;

    private sealed record DirectOrigin : ConnectionOrigin;

    private sealed class OriginConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(ConnectionOrigin);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            new OriginConverter();

        private sealed class OriginConverter : JsonConverter<ConnectionOrigin>
        {
            public override ConnectionOrigin Read(
                ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                using var document = JsonDocument.ParseValue(ref reader);
                return document.RootElement.GetProperty("kind").GetString() switch
                {
                    "direct" => new DirectOrigin(),
                    var kind => throw new JsonException($"Unknown connection origin '{kind}'.")
                };
            }

            public override void Write(
                Utf8JsonWriter writer, ConnectionOrigin value, JsonSerializerOptions options) =>
                throw new NotSupportedException();
        }
    }
}
