// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Com.Daml.Ledger.Api.V2;
using Xunit;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoField = Com.Daml.Ledger.Api.V2.RecordField;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using ProtoRecord = Com.Daml.Ledger.Api.V2.Record;

namespace Canton.Ledger.Grpc.Client.Tests;

public class LedgerClientPointReadDecodeTests
{
    private const string LookupDescription = "offset 42";

    private const string MalformedResponsePrefix = "Malformed response from ledger: ";

    private static readonly ProtoIdentifier TemplateId = new()
    {
        PackageId = "pkg",
        ModuleName = "Module",
        EntityName = "Entity",
    };

    [Fact]
    public void ProjectPointRead_relabels_a_wire_date_outside_the_range_its_Daml_LF_type_allows()
    {
        var createArguments = new ProtoRecord();
        createArguments.Fields.Add(new ProtoField { Label = "when", Value = new Value { Date = int.MaxValue } });
        var response = TransactionResponse(new Event
        {
            Created = new ProtoCreatedEvent
            {
                NodeId = 0,
                ContractId = "00aa",
                TemplateId = TemplateId,
                CreateArguments = createArguments,
            },
        });

        var act = () => LedgerClient.ProjectPointRead(
            response, LookupDescription, GrpcTransactionTreeProjector.Project);

        var thrown = act.Should().Throw<InvalidOperationException>().Which;
        thrown.Message.Should().StartWith(
            $"{MalformedResponsePrefix}the transaction at {LookupDescription} could not be decoded: "
            + "Days since epoch must resolve to a date within the Daml-LF Date range");
        thrown.InnerException.Should().BeOfType<MalformedResponseException>()
            .Which.InnerException.Should().BeOfType<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ProjectPointRead_leaves_a_failure_no_wire_value_could_have_raised_untouched()
    {
        var ours = new NotSupportedException("Unknown right kind: KindOneofCase.None");

        var act = () => LedgerClient.ProjectPointRead<int>(
            TransactionResponse(), LookupDescription, _ => throw ours);

        act.Should().Throw<NotSupportedException>().Which.Should().BeSameAs(ours);
    }

    [Fact]
    public void ProjectPointRead_relabels_a_transaction_whose_created_event_states_no_template_id()
    {
        var response = TransactionResponse(new Event
        {
            Created = new ProtoCreatedEvent { NodeId = 0, ContractId = "00aa", CreateArguments = new ProtoRecord() },
        });

        var act = () => LedgerClient.ProjectPointRead(
            response, LookupDescription, GrpcTransactionResultProjector.Project);

        var thrown = act.Should().Throw<InvalidOperationException>().Which;
        thrown.Message.Should().Be(
            $"{MalformedResponsePrefix}the transaction at {LookupDescription} could not be decoded: "
            + "CreatedEvent for contract '00aa' has no template_id, "
            + "though the Ledger API marks the field as required.");
        thrown.InnerException.Should().BeOfType<MalformedResponseException>();
    }

    [Fact]
    public void ProjectPointRead_relabels_a_transaction_carrying_a_record_field_with_no_value()
    {
        var createArguments = new ProtoRecord();
        createArguments.Fields.Add(new ProtoField { Label = "amount" });
        var response = TransactionResponse(new Event
        {
            Created = new ProtoCreatedEvent
            {
                NodeId = 0,
                ContractId = "00aa",
                TemplateId = TemplateId,
                CreateArguments = createArguments,
            },
        });

        var act = () => LedgerClient.ProjectPointRead(
            response, LookupDescription, GrpcTransactionTreeProjector.Project);

        var thrown = act.Should().Throw<InvalidOperationException>().Which;
        thrown.Message.Should().Be(
            $"{MalformedResponsePrefix}the transaction at {LookupDescription} could not be decoded: "
            + "Record field 'amount' has no Value set.");
        thrown.InnerException!.InnerException.Should()
            .BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be("Record field 'amount' has no Value set.");
    }

    [Fact]
    public void ProjectPointRead_rejects_an_update_that_is_not_a_transaction()
    {
        var response = new GetUpdateResponse { Reassignment = new Reassignment() };

        var act = () => LedgerClient.ProjectPointRead<int>(response, LookupDescription, _ => 0);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*is a Reassignment, not a Transaction*");
    }

    private static GetUpdateResponse TransactionResponse(params Event[] events)
    {
        var transaction = new Transaction { UpdateId = "update-1", Offset = 42L, CommandId = "cmd-1" };
        transaction.Events.AddRange(events);
        return new GetUpdateResponse { Transaction = transaction };
    }
}
