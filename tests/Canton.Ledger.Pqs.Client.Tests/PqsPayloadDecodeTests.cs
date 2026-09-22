// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Xunit;

namespace Canton.Ledger.Pqs.Client.Tests;

public class PqsPayloadDecodeTests
{
    private static Contract<PqsLedgerEntry> Decode(string payloadJson) =>
        PqsClient.DeserializeContract<PqsLedgerEntry>("00abc123", payloadJson);

    [Fact]
    public void DeserializeContract_round_trips_string_encoded_Int64_and_Numeric()
    {
        var contract = Decode("""{"owner":"alice::ns1","quantity":"9007199254740993","price":"1234.5678","note":"first"}""");

        contract.Id.Value.Should().Be("00abc123");
        contract.Data.Owner.Should().Be("alice::ns1");
        contract.Data.Quantity.Should().Be(9007199254740993L);
        contract.Data.Price.Should().Be(1234.5678m);
        contract.Data.Note.Should().Be("first");
    }

    [Fact]
    public void DeserializeContract_reads_an_explicit_JSON_null_optional_as_absent()
    {
        var contract = Decode("""{"owner":"alice","quantity":"1","price":"2","note":null}""");

        contract.Data.Note.Should().BeNull();
    }

    [Fact]
    public void DeserializeContract_refuses_an_omitted_optional_key_and_names_the_field()
    {
        var act = () => Decode("""{"owner":"alice","quantity":"1","price":"2"}""");

        act.Should().Throw<JsonException>().Which.Message.Should().Contain("note");
    }

    [Fact]
    public void DeserializeContract_refuses_a_bare_JSON_number_for_an_Int64_and_names_the_field()
    {
        var act = () => Decode("""{"owner":"alice","quantity":42,"price":"2","note":null}""");

        act.Should().Throw<JsonException>().Which.Message.Should().Contain("quantity");
    }

    [Fact]
    public void DeserializeContract_refuses_a_bare_JSON_number_for_a_Numeric_and_names_the_field()
    {
        var act = () => Decode("""{"owner":"alice","quantity":"1","price":1.5,"note":null}""");

        act.Should().Throw<JsonException>().Which.Message.Should().Contain("price");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("  null  ")]
    public void DeserializeContract_refuses_a_null_payload(string payloadJson)
    {
        var act = () => Decode(payloadJson);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void DeserializeContract_refuses_a_row_larger_than_16_MiB()
    {
        var oversizedNote = new string('x', 16 * 1024 * 1024);
        var payloadJson = $$"""{"owner":"alice","quantity":"1","price":"2","note":"{{oversizedNote}}"}""";

        var act = () => Decode(payloadJson);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void DeserializeContract_accepts_a_row_just_under_16_MiB()
    {
        var largeNote = new string('x', 16 * 1024 * 1024 - 200);
        var payloadJson = $$"""{"owner":"alice","quantity":"1","price":"2","note":"{{largeNote}}"}""";

        var contract = Decode(payloadJson);

        contract.Data.Note.Should().HaveLength(16 * 1024 * 1024 - 200);
    }

    [Fact]
    public void DeserializeInterfaceContract_refuses_an_omitted_view_key_and_names_the_field()
    {
        var act = () => PqsClient.DeserializeInterfaceContract<ISampleInterface, SampleView>("00cid", "{}");

        act.Should().Throw<JsonException>().Which.Message.Should().Contain("amount");
    }

    [Fact]
    public void DeserializeInterfaceContract_refuses_a_bare_JSON_number_for_a_Numeric_view_field()
    {
        var act = () => PqsClient.DeserializeInterfaceContract<ISampleInterface, SampleView>(
            "00cid", """{"amount":123.45}""");

        act.Should().Throw<JsonException>().Which.Message.Should().Contain("amount");
    }

    [Fact]
    public void PqsClientOptions_declares_no_JSON_serializer_options()
    {
        typeof(PqsClientOptions).GetProperty("JsonSerializerOptions").Should().BeNull();
        typeof(PqsClientOptions).GetMethod("CreateDefaultJsonSerializerOptions").Should().BeNull();
    }

    [Fact]
    public void PqsClient_declares_no_shared_default_JSON_serializer_options()
    {
        var bindings = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        typeof(PqsClient).GetField("DefaultJsonSerializerOptions", bindings).Should().BeNull();
        typeof(PqsClient).GetMethod("CreateSharedDefaultJsonOptions", bindings).Should().BeNull();
    }

    [Theory]
    [InlineData(typeof(IPqsClient))]
    [InlineData(typeof(PqsClient))]
    public void Payload_returning_template_methods_constrain_T_to_IDamlRecord(Type clientType)
    {
        var payloadReturning = TemplateMethodsOf(clientType).Where(m => m.Name != "ExistsAsync").ToList();

        payloadReturning.Should().HaveCount(6);
        payloadReturning.Should().OnlyContain(m => ConstrainsToDamlRecord(m));
    }

    [Theory]
    [InlineData(typeof(IPqsClient))]
    [InlineData(typeof(PqsClient))]
    public void ExistsAsync_needs_no_payload_decoder_and_leaves_T_unconstrained_by_IDamlRecord(Type clientType)
    {
        var exists = TemplateMethodsOf(clientType).Single(m => m.Name == "ExistsAsync");

        ConstrainsToDamlRecord(exists).Should().BeFalse();
    }

    internal static IEnumerable<MethodInfo> TemplateMethodsOf(Type clientType) =>
        clientType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 1);

    internal static bool ConstrainsToDamlRecord(MethodInfo method) =>
        method.GetGenericArguments()[0]
            .GetGenericParameterConstraints()
            .Any(c => c.IsGenericType && c.GetGenericTypeDefinition() == typeof(IDamlRecord<>));
}
