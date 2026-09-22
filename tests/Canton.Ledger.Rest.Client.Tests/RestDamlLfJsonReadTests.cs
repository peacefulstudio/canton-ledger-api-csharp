// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using WireActiveContracts = Canton.Ledger.Rest.Client.Raw.GetActiveContractsResponse;
using WireRecord = Canton.Ledger.Rest.Client.Raw.Record;
using WireTransaction = Canton.Ledger.Rest.Client.Raw.SubmitAndWaitForTransactionResponse;
using WireUpdate = Canton.Ledger.Rest.Client.Raw.GetUpdatesResponse;
using WireValue = Canton.Ledger.Rest.Client.Raw.Value;

namespace Canton.Ledger.Rest.Client.Tests;

public class RestDamlLfJsonReadTests
{
    private sealed record TextResult([property: DamlFieldAttribute("text")] string Text) : IDamlRecord<TextResult>
    {
        public DamlRecord ToRecord() => throw new NotSupportedException();
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(
                json,
                context,
                ("text", DamlLfJsonDecoders.ReadText));
        public static TextResult FromRecord(DamlRecord record) => throw new NotSupportedException();
    }

    private sealed record OwnerTextResult(
        [property: DamlFieldAttribute("owner")] Party Owner,
        [property: DamlFieldAttribute("text")] string Text) : IDamlRecord<OwnerTextResult>
    {
        public DamlRecord ToRecord() => throw new NotSupportedException();
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(
                json,
                context,
                ("owner", DamlLfJsonDecoders.ReadParty),
                ("text", DamlLfJsonDecoders.ReadText));
        public static OwnerTextResult FromRecord(DamlRecord record) => throw new NotSupportedException();
    }

    private sealed record PartyResult([property: DamlFieldAttribute("party")] Party Party) : IDamlRecord<PartyResult>
    {
        public DamlRecord ToRecord() => throw new NotSupportedException();
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(
                json,
                context,
                ("party", DamlLfJsonDecoders.ReadParty));
        public static PartyResult FromRecord(DamlRecord record) => throw new NotSupportedException();
    }

    private sealed record NumericResult([property: DamlFieldAttribute("numeric")] decimal Numeric) : IDamlRecord<NumericResult>
    {
        public DamlRecord ToRecord() => throw new NotSupportedException();
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(
                json,
                context,
                ("numeric", DamlLfJsonDecoders.ReadNumeric));
        public static NumericResult FromRecord(DamlRecord record) => throw new NotSupportedException();
    }

    private sealed record DateResult([property: DamlFieldAttribute("date")] DateOnly Date) : IDamlRecord<DateResult>
    {
        public DamlRecord ToRecord() => throw new NotSupportedException();
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(
                json,
                context,
                ("date", DamlLfJsonDecoders.ReadDate));
        public static DateResult FromRecord(DamlRecord record) => throw new NotSupportedException();
    }

    private sealed record OwnerDateResult(
        [property: DamlFieldAttribute("owner")] Party Owner,
        [property: DamlFieldAttribute("date")] DateOnly Date) : IDamlRecord<OwnerDateResult>
    {
        public DamlRecord ToRecord() => throw new NotSupportedException();
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(
                json,
                context,
                ("owner", DamlLfJsonDecoders.ReadParty),
                ("date", DamlLfJsonDecoders.ReadDate));
        public static OwnerDateResult FromRecord(DamlRecord record) => throw new NotSupportedException();
    }

    private sealed record TimestampResult(
        [property: DamlFieldAttribute("timestamp")] DateTimeOffset Timestamp) : IDamlRecord<TimestampResult>
    {
        public DamlRecord ToRecord() => throw new NotSupportedException();
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(
                json,
                context,
                ("timestamp", DamlLfJsonDecoders.ReadTimestamp));
        public static TimestampResult FromRecord(DamlRecord record) => throw new NotSupportedException();
    }

    private sealed record ListResult(
        [property: DamlFieldAttribute("list")] IReadOnlyList<string> List) : IDamlRecord<ListResult>
    {
        public DamlRecord ToRecord() => throw new NotSupportedException();
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(
                json,
                context,
                ("list", (element, elementContext) => DamlLfJsonDecoders.ReadList(element, elementContext, DamlLfJsonDecoders.ReadText)));
        public static ListResult FromRecord(DamlRecord record) => throw new NotSupportedException();
    }

    private sealed record FieldsTemplate([property: DamlFieldAttribute("fields")] string Fields)
        : ITemplate, IDamlRecord<FieldsTemplate>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("tmpl-pkg", "Sample.Token", "FieldsHolding");
        public static string PackageId => "tmpl-pkg";
        public static string PackageName => "token-impl";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => throw new NotSupportedException();
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(
                json,
                context,
                ("fields", DamlLfJsonDecoders.ReadText));
        public static FieldsTemplate FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("fields").As<DamlText>().Value);
    }

    private sealed record RecordIdTemplate([property: DamlFieldAttribute("recordId")] string RecordId)
        : ITemplate, IDamlRecord<RecordIdTemplate>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("tmpl-pkg", "Sample.Token", "RecordIdHolding");
        public static string PackageId => "tmpl-pkg";
        public static string PackageName => "token-impl";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => throw new NotSupportedException();
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(
                json,
                context,
                ("recordId", DamlLfJsonDecoders.ReadText));
        public static RecordIdTemplate FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("recordId").As<DamlText>().Value);
    }

    private sealed record ResultsTemplate : ITemplate, IDamlRecord<ResultsTemplate>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("tmpl-pkg", "Sample.Token", "ResultsHolding");
        public static string PackageId => "tmpl-pkg";
        public static string PackageName => "token-impl";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => throw new NotSupportedException();
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(json, context);
        public static ResultsTemplate FromRecord(DamlRecord record) => throw new NotSupportedException();

        public static Choice<ResultsTemplate, CirceReceipt, TextResult> ChoiceTextResult { get; } = ChoiceReturning<TextResult>();
        public static Choice<ResultsTemplate, CirceReceipt, OwnerTextResult> ChoiceOwnerTextResult { get; } = ChoiceReturning<OwnerTextResult>();
        public static Choice<ResultsTemplate, CirceReceipt, PartyResult> ChoicePartyResult { get; } = ChoiceReturning<PartyResult>();
        public static Choice<ResultsTemplate, CirceReceipt, NumericResult> ChoiceNumericResult { get; } = ChoiceReturning<NumericResult>();
        public static Choice<ResultsTemplate, CirceReceipt, DateResult> ChoiceDateResult { get; } = ChoiceReturning<DateResult>();
        public static Choice<ResultsTemplate, CirceReceipt, OwnerDateResult> ChoiceOwnerDateResult { get; } = ChoiceReturning<OwnerDateResult>();
        public static Choice<ResultsTemplate, CirceReceipt, TimestampResult> ChoiceTimestampResult { get; } = ChoiceReturning<TimestampResult>();
        public static Choice<ResultsTemplate, CirceReceipt, ListResult> ChoiceListResult { get; } = ChoiceReturning<ListResult>();

        private static Choice<ResultsTemplate, CirceReceipt, TResult> ChoiceReturning<TResult>()
            where TResult : IDamlRecord<TResult> => new()
        {
            Name = new ChoiceName(typeof(TResult).Name),
            Consuming = false,
            ArgumentEncoder = _ => throw new NotSupportedException(),
            ArgumentDecoder = _ => throw new NotSupportedException(),
            ArgumentJsonReader = CirceReceipt.__ReadDamlLfJson,
            ResultDecoder = _ => throw new NotSupportedException(),
            ResultJsonReader = TResult.__ReadDamlLfJson,
        };
    }

    private sealed record PartyKey([property: DamlFieldAttribute("party")] Party Party) : IDamlRecord<PartyKey>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("party", Party.ToDamlValue()));
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(
                json,
                context,
                ("party", DamlLfJsonDecoders.ReadParty));
        public static PartyKey FromRecord(DamlRecord record) =>
            new(Party.FromDamlValue(record.GetRequiredField("party").As<DamlParty>()));
    }

    private sealed record DateKey([property: DamlFieldAttribute("date")] DateOnly Date) : IDamlRecord<DateKey>
    {
        public DamlRecord ToRecord() => throw new NotSupportedException();
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(
                json,
                context,
                ("date", DamlLfJsonDecoders.ReadDate));
        public static DateKey FromRecord(DamlRecord record) =>
            new((DateOnly)record.GetRequiredField("date").As<DamlDate>());
    }

    private sealed record PartyKeyedTemplate([property: DamlFieldAttribute("owner")] Party Owner)
        : ITemplate, IDamlRecord<PartyKeyedTemplate>, IHasKey<PartyKeyedTemplate, PartyKey>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("tmpl-pkg", "Sample.Token", "PartyKeyedHolding");
        public static string PackageId => "tmpl-pkg";
        public static string PackageName => "token-impl";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => throw new NotSupportedException();
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(
                json,
                context,
                ("owner", DamlLfJsonDecoders.ReadParty));
        public static PartyKeyedTemplate FromRecord(DamlRecord record) =>
            new(Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()));

        public static KeyDescriptor<PartyKeyedTemplate, PartyKey> Key { get; } = new()
        {
            KeyEncoder = key => key.ToRecord(),
            KeyDecoder = value => PartyKey.FromRecord(value.As<DamlRecord>()),
            KeyJsonReader = PartyKey.__ReadDamlLfJson,
        };
    }

    private sealed record DateKeyedTemplate([property: DamlFieldAttribute("owner")] Party Owner)
        : ITemplate, IDamlRecord<DateKeyedTemplate>, IHasKey<DateKeyedTemplate, DateKey>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("tmpl-pkg", "Sample.Token", "DateKeyedHolding");
        public static string PackageId => "tmpl-pkg";
        public static string PackageName => "token-impl";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => throw new NotSupportedException();
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(
                json,
                context,
                ("owner", DamlLfJsonDecoders.ReadParty));
        public static DateKeyedTemplate FromRecord(DamlRecord record) =>
            new(Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()));

        public static KeyDescriptor<DateKeyedTemplate, DateKey> Key { get; } = new()
        {
            KeyEncoder = key => key.ToRecord(),
            KeyDecoder = value => DateKey.FromRecord(value.As<DamlRecord>()),
            KeyJsonReader = DateKey.__ReadDamlLfJson,
        };
    }

    private sealed record HoldingInterface : IDamlInterface
    {
        public static RuntimeIdentifier InterfaceId { get; } = new("iface-pkg", "Token.Api", "IHolding");
        public static string PackageId => "iface-pkg";
        public static string PackageName => "token-iface";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(InterfaceId, DamlTypeKind.Interface, PackageName);
        public DamlRecord ToRecord() => new(InterfaceId, []);
    }

    private sealed record PartyView([property: DamlFieldAttribute("party")] Party Party) : IDamlRecord<PartyView>
    {
        public DamlRecord ToRecord() => throw new NotSupportedException();
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(
                json,
                context,
                ("party", DamlLfJsonDecoders.ReadParty));
        public static PartyView FromRecord(DamlRecord record) => throw new NotSupportedException();
    }

    public static TheoryData<string> DamlLfJsonObjects() => new(
        """{"date":"2026-01-01"}""",
        """{"owner":"alice::ns1","date":"2026-01-01"}""",
        """{"text":"hello"}""",
        """{"text":5}""",
        """{"party":"alice::ns1"}""",
        """{"numeric":"10.5"}""",
        """{"list":["a","b"]}""",
        """{"timestamp":"2026-01-01T00:00:00Z"}""",
        """{"fields":"hello"}""",
        """{"fields":[{"label":"owner","value":{"party":"alice::ns1"}}]}""",
        """{"recordId":"hello"}""",
        """{"recordId":{"packageId":"p","moduleName":"M","entityName":"E"},"fields":7}""",
        """{"owner":"alice::ns1","amount":"10.5"}""");

    private static Raw.Transaction TransactionWithExerciseResult(string choice, string exerciseResultJson) =>
        JsonSerializer.Deserialize<WireTransaction>(
            $$$"""
            {"transaction":{"updateId":"upd-1","offset":"1","events":[{"ExercisedEvent":{
              "offset":"1","contractId":"00holding",
              "templateId":{"packageId":"tmpl-pkg","moduleName":"Sample.Token","entityName":"ResultsHolding"},
              "choice":"{{{choice}}}","choiceArgument":{},"actingParties":["alice::ns1"],"consuming":false,
              "witnessParties":["alice::ns1"],"exerciseResult":{{{exerciseResultJson}}}}}]}}
            """,
            RestRefitSettings.SerializerOptions)!.Transaction;

    private static DamlRecord TypedResultOf<TResult>(string exerciseResultJson) =>
        RestTransactionResultProjector.Project(TransactionWithExerciseResult(typeof(TResult).Name, exerciseResultJson))
            .ExercisedEvents.Should().ContainSingle().Subject
            .ExerciseResult.Should().BeOfType<DamlRecord>().Subject;

    private static string CreatedEventJson(
        string createArgumentJson, string? contractKeyJson = null, string? viewValueJson = null, string entityName = "Holding") =>
        "{\"offset\":\"7\",\"nodeId\":0,\"contractId\":\"00holding\","
        + "\"templateId\":{\"packageId\":\"tmpl-pkg\",\"moduleName\":\"Sample.Token\",\"entityName\":\"" + entityName + "\"},"
        + "\"createArgument\":" + createArgumentJson + ","
        + (contractKeyJson is null ? "" : "\"contractKey\":" + contractKeyJson + ",\"contractKeyHash\":\"AQID\",")
        + (viewValueJson is null
            ? ""
            : "\"interfaceViews\":[{\"interfaceId\":{\"packageId\":\"iface-pkg\",\"moduleName\":\"Token.Api\",\"entityName\":\"IHolding\"},"
              + "\"viewStatus\":{\"code\":0,\"message\":\"\"},\"viewValue\":" + viewValueJson + "}],")
        + "\"witnessParties\":[\"alice::ns1\"]}";

    private static Raw.Transaction StreamTransactionOf(string createdEventJson) =>
        JsonSerializer.Deserialize<WireUpdate>(
            "{\"update\":{\"Transaction\":{\"value\":{\"offset\":\"7\",\"synchronizerId\":\"sync-1\",\"events\":[{\"CreatedEvent\":"
            + createdEventJson + "}]}}}}",
            RestRefitSettings.SerializerOptions)!.Update.Transaction;

    private static WireActiveContracts ActiveContractOf(string createdEventJson) =>
        JsonSerializer.Deserialize<WireActiveContracts>(
            "{\"contractEntry\":{\"JsActiveContract\":{\"createdEvent\":" + createdEventJson
            + ",\"synchronizerId\":\"sync-1\",\"reassignmentCounter\":\"0\"}}}",
            RestRefitSettings.SerializerOptions)!;

    private static TPayload StreamPayloadOf<TPayload>(string createdEventJson)
        where TPayload : ITemplate, IDamlRecord<TPayload> =>
        RestContractStreamProjector.ProjectTransactionEvents<TPayload>(StreamTransactionOf(createdEventJson))
            .Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<TPayload>.Created>().Subject.Payload;

    private static TPayload ActivePayloadOf<TPayload>(string createdEventJson)
        where TPayload : ITemplate, IDamlRecord<TPayload> =>
        RestContractStreamProjector.ProjectActiveContractEntry<TPayload>(ActiveContractOf(createdEventJson))
            .Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<TPayload>.Created>().Subject.Payload;

    private static void ShouldHoldTheRawJson(IDictionary<string, object> additionalProperties, string json)
    {
        additionalProperties.Should().ContainSingle().Which.Key.Should().Be("idiomatic");
        JsonNode.DeepEquals(JsonNode.Parse((string)additionalProperties["idiomatic"]), JsonNode.Parse(json))
            .Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(DamlLfJsonObjects))]
    public void Deserialize_reads_any_Daml_LF_JSON_object_as_a_wire_Value_holding_the_raw_JSON(string json)
    {
        var value = JsonSerializer.Deserialize<WireValue>(json, RestRefitSettings.SerializerOptions)!;

        ShouldHoldTheRawJson(value.AdditionalProperties, json);
        value.Text.Should().BeNull();
        value.Party.Should().BeNull();
        value.Numeric.Should().BeNull();
        value.Date.Should().BeNull();
        value.Timestamp.Should().BeNull();
        value.List.Should().BeNull();
        value.Record.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(DamlLfJsonObjects))]
    public void Deserialize_reads_any_Daml_LF_JSON_object_as_a_wire_Record_holding_the_raw_JSON(string json)
    {
        var record = JsonSerializer.Deserialize<WireRecord>(json, RestRefitSettings.SerializerOptions)!;

        ShouldHoldTheRawJson(record.AdditionalProperties, json);
        record.Fields.Should().BeNull();
        record.RecordId.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(DamlLfJsonObjects))]
    public void Deserialize_reads_a_whole_stream_page_whose_payloads_are_any_Daml_LF_JSON_object(string json)
    {
        var act = () => StreamTransactionOf(CreatedEventJson(json, contractKeyJson: json, viewValueJson: json));

        act.Should().NotThrow();
    }

    [Fact]
    public void Project_decodes_a_single_field_record_result_whose_field_is_named_text()
    {
        TypedResultOf<TextResult>("""{"text":"hello"}""")
            .GetRequiredField("text").Should().Be(new DamlText("hello"));
    }

    [Fact]
    public void Project_keeps_every_field_of_a_multi_field_record_result_with_a_field_named_text()
    {
        var record = TypedResultOf<OwnerTextResult>("""{"owner":"alice::ns1","text":"hello"}""");

        record.Fields.Should().HaveCount(2);
        record.GetRequiredField("owner").Should().Be(new DamlParty("alice::ns1"));
        record.GetRequiredField("text").Should().Be(new DamlText("hello"));
    }

    [Fact]
    public void Project_decodes_a_record_result_whose_field_is_named_party()
    {
        TypedResultOf<PartyResult>("""{"party":"alice::ns1"}""")
            .GetRequiredField("party").Should().Be(new DamlParty("alice::ns1"));
    }

    [Fact]
    public void Project_decodes_a_record_result_whose_field_is_named_numeric()
    {
        TypedResultOf<NumericResult>("""{"numeric":"10.5"}""")
            .GetRequiredField("numeric").Should().Be(new DamlNumeric(10.5m));
    }

    [Fact]
    public void Project_decodes_a_record_result_whose_field_is_named_date()
    {
        TypedResultOf<DateResult>("""{"date":"2026-01-01"}""")
            .GetRequiredField("date").Should().Be(new DamlDate(new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public void Project_decodes_a_multi_field_record_result_with_a_field_named_date()
    {
        var record = TypedResultOf<OwnerDateResult>("""{"owner":"alice::ns1","date":"2026-01-01"}""");

        record.GetRequiredField("owner").Should().Be(new DamlParty("alice::ns1"));
        record.GetRequiredField("date").Should().Be(new DamlDate(new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public void Project_decodes_a_record_result_whose_field_is_named_timestamp()
    {
        TypedResultOf<TimestampResult>("""{"timestamp":"2026-01-01T00:00:00Z"}""")
            .GetRequiredField("timestamp").Should().Be(
                new DamlTimestamp(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Project_decodes_a_record_result_whose_field_is_named_list()
    {
        TypedResultOf<ListResult>("""{"list":["a","b"]}""")
            .GetRequiredField("list").Should().BeEquivalentTo(
                new DamlList([new DamlText("a"), new DamlText("b")]));
    }

    [Fact]
    public void ProjectTransactionEvents_decodes_a_record_contract_key_whose_field_is_named_party()
    {
        var created = RestContractStreamProjector.ProjectTransactionEvents<PartyKeyedTemplate>(
                StreamTransactionOf(CreatedEventJson("""{"owner":"alice::ns1"}""", contractKeyJson: """{"party":"alice::ns1"}""", entityName: "PartyKeyedHolding")))
            .Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<PartyKeyedTemplate>.Created>().Subject;

        created.Key!.Value.Should().BeOfType<DamlRecord>()
            .Which.GetRequiredField("party").Should().Be(new DamlParty("alice::ns1"));
    }

    [Fact]
    public void ProjectTransactionEvents_decodes_a_record_contract_key_whose_field_is_named_date()
    {
        var created = RestContractStreamProjector.ProjectTransactionEvents<DateKeyedTemplate>(
                StreamTransactionOf(CreatedEventJson("""{"owner":"alice::ns1"}""", contractKeyJson: """{"date":"2026-01-01"}""", entityName: "DateKeyedHolding")))
            .Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<DateKeyedTemplate>.Created>().Subject;

        created.Key!.Value.Should().BeOfType<DamlRecord>()
            .Which.GetRequiredField("date").Should().Be(new DamlDate(new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public void ProjectActiveContractEntry_decodes_a_record_contract_key_whose_field_is_named_party()
    {
        var created = RestContractStreamProjector.ProjectActiveContractEntry<PartyKeyedTemplate>(
                ActiveContractOf(CreatedEventJson("""{"owner":"alice::ns1"}""", contractKeyJson: """{"party":"alice::ns1"}""", entityName: "PartyKeyedHolding")))
            .Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<PartyKeyedTemplate>.Created>().Subject;

        created.Key!.Value.Should().BeOfType<DamlRecord>()
            .Which.GetRequiredField("party").Should().Be(new DamlParty("alice::ns1"));
    }

    [Fact]
    public void ProjectTransactionEvents_decodes_a_create_argument_whose_field_is_named_fields()
    {
        StreamPayloadOf<FieldsTemplate>(CreatedEventJson("""{"fields":"hello"}""", entityName: "FieldsHolding"))
            .Fields.Should().Be("hello");
    }

    [Fact]
    public void ProjectTransactionEvents_decodes_a_create_argument_whose_field_is_named_recordId()
    {
        StreamPayloadOf<RecordIdTemplate>(CreatedEventJson("""{"recordId":"hello"}""", entityName: "RecordIdHolding"))
            .RecordId.Should().Be("hello");
    }

    [Fact]
    public void ProjectActiveContractEntry_decodes_a_create_argument_whose_field_is_named_fields()
    {
        ActivePayloadOf<FieldsTemplate>(CreatedEventJson("""{"fields":"hello"}""", entityName: "FieldsHolding"))
            .Fields.Should().Be("hello");
    }

    [Fact]
    public void ProjectActiveContractEntry_decodes_a_create_argument_whose_field_is_named_recordId()
    {
        ActivePayloadOf<RecordIdTemplate>(CreatedEventJson("""{"recordId":"hello"}""", entityName: "RecordIdHolding"))
            .RecordId.Should().Be("hello");
    }

    [Fact]
    public void TryGetInterfaceViewRecord_decodes_a_view_value_whose_field_is_named_party()
    {
        var created = ActiveContractOf(CreatedEventJson(
                """{"owner":"alice::ns1"}""", viewValueJson: """{"party":"alice::ns1"}"""))
            .ContractEntry!.JsActiveContract!.CreatedEvent!;

        RestMarkerMatcher<HoldingInterface>.TryGetInterfaceViewRecord<PartyView>(created, out var view)
            .Should().BeTrue();

        view.Fields.Should().ContainSingle();
        view.GetRequiredField("party").Should().Be(new DamlParty("alice::ns1"));
    }
}
