// Copyright 2026 Peaceful Studio OÜ

using Com.Daml.Ledger.Api.V2;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using FluentAssertions;
using Xunit;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using ProtoRecord = Com.Daml.Ledger.Api.V2.Record;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Grpc.Client.Tests;

public class TransactionResultProjectorTests
{
    [Fact]
    public void Project_throws_when_response_Transaction_is_null()
    {
        var responseWithNoTransaction = new SubmitAndWaitForTransactionResponse();

        var act = () => TransactionResultProjector.Project(responseWithNoTransaction);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no Transaction was present*");
    }

    [Fact]
    public void Project_populates_InterfaceIds_from_interface_views()
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00holding",
            TemplateId = new ProtoIdentifier { PackageId = "impl-pkg", ModuleName = "Token.Holding", EntityName = "Holding" },
            CreateArguments = new ProtoRecord(),
        };
        created.InterfaceViews.Add(new InterfaceView
        {
            InterfaceId = new ProtoIdentifier { PackageId = "iface-pkg", ModuleName = "Token.Api", EntityName = "IHolding" },
        });

        var result = TransactionResultProjector.Project(ResponseWith(created));

        var contract = result.CreatedContracts.Should().ContainSingle().Subject;
        contract.InterfaceIds.Should().ContainSingle();
        contract.InterfaceIds[0].ModuleName.Should().Be("Token.Api");
        contract.InterfaceIds[0].EntityName.Should().Be("IHolding");
    }

    [Fact]
    public void Project_yields_empty_InterfaceIds_when_no_interface_views()
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00plain",
            TemplateId = new ProtoIdentifier { PackageId = "impl-pkg", ModuleName = "Token.Holding", EntityName = "Holding" },
            CreateArguments = new ProtoRecord(),
        };

        var result = TransactionResultProjector.Project(ResponseWith(created));

        result.CreatedContracts.Should().ContainSingle()
            .Which.InterfaceIds.Should().BeEmpty();
    }

    [Fact]
    public void ProjectToContractId_matches_interface_marker_by_interface_view_ignoring_package_id()
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00holding",
            TemplateId = new ProtoIdentifier { PackageId = "impl-pkg-2", ModuleName = "Token.Holding", EntityName = "Holding" },
            CreateArguments = new ProtoRecord(),
        };
        created.InterfaceViews.Add(new InterfaceView
        {
            InterfaceId = new ProtoIdentifier { PackageId = "any-pkg", ModuleName = "Token.Api", EntityName = "IHolding" },
        });
        var transactionResult = TransactionResultProjector.Project(ResponseWith(created));

        var outcome = TransactionResultProjector.ProjectToContractId<IHolding>(
            new ExerciseOutcome<TransactionResult>.One(transactionResult));

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<IHolding>>.One>()
            .Which.Result.Value.Should().Be("00holding");
    }

    [Fact]
    public void ProjectToContractId_returns_None_for_interface_marker_when_no_matching_view()
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00plain",
            TemplateId = new ProtoIdentifier { PackageId = "impl-pkg", ModuleName = "Token.Holding", EntityName = "Holding" },
            CreateArguments = new ProtoRecord(),
        };
        var transactionResult = TransactionResultProjector.Project(ResponseWith(created));

        var outcome = TransactionResultProjector.ProjectToContractId<IHolding>(
            new ExerciseOutcome<TransactionResult>.One(transactionResult));

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<IHolding>>.None>();
    }

    [Fact]
    public void ProjectToContractId_throws_when_marker_is_neither_ITemplate_nor_IDamlInterface()
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = "00bare",
            TemplateId = new ProtoIdentifier { PackageId = "impl-pkg", ModuleName = "Token.Holding", EntityName = "Holding" },
            CreateArguments = new ProtoRecord(),
        };
        var transactionResult = TransactionResultProjector.Project(ResponseWith(created));

        var act = () => TransactionResultProjector.ProjectToContractId<BareDamlType>(
            new ExerciseOutcome<TransactionResult>.One(transactionResult));

        act.Should().Throw<TypeInitializationException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*is neither IDamlInterface nor ITemplate*");
    }

    private static SubmitAndWaitForTransactionResponse ResponseWith(ProtoCreatedEvent created)
    {
        var transaction = new Transaction { UpdateId = "u-iface", Offset = 1L };
        transaction.Events.Add(new Event { Created = created });
        return new SubmitAndWaitForTransactionResponse { Transaction = transaction };
    }

    internal sealed record IHolding : IDamlInterface
    {
        public static RuntimeIdentifier InterfaceId { get; } = new("iface-pkg", "Token.Api", "IHolding");
        public static string PackageId => "iface-pkg";
        public static string PackageName => "token-api";
        public static Version PackageVersion { get; } = new(0, 1, 0);

        public DamlRecord ToRecord() => DamlRecord.Create();
    }

    internal sealed record BareDamlType : IDamlType;
}
