// Copyright 2026 Peaceful Studio OÜ

using Com.Daml.Ledger.Api.V2;
using FluentAssertions;
using Xunit;

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
}
