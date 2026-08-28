// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using AwesomeAssertions;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Outcomes;
using Grpc.Core;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public class GrpcStatusMappingTests
{
    [Theory]
    [InlineData(DamlErrorCategory.Unknown, StatusCode.Unknown)]
    [InlineData(DamlErrorCategory.TransientServerFailure, StatusCode.Unavailable)]
    [InlineData(DamlErrorCategory.ContentionOnSharedResources, StatusCode.Aborted)]
    [InlineData(DamlErrorCategory.DeadlineExceededRequestStateUnknown, StatusCode.DeadlineExceeded)]
    [InlineData(DamlErrorCategory.SystemInternalAssumptionViolated, StatusCode.Internal)]
    [InlineData(DamlErrorCategory.MaliciousOrFaultyBehaviour, StatusCode.Unknown)]
    [InlineData(DamlErrorCategory.AuthInterceptorInvalidAuthenticationCredentials, StatusCode.Unauthenticated)]
    [InlineData(DamlErrorCategory.AuthorizationChecksFailed, StatusCode.PermissionDenied)]
    [InlineData(DamlErrorCategory.InvalidIndependentOfSystemState, StatusCode.InvalidArgument)]
    [InlineData(DamlErrorCategory.InvalidGivenCurrentSystemStateOther, StatusCode.FailedPrecondition)]
    [InlineData(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists, StatusCode.AlreadyExists)]
    [InlineData(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing, StatusCode.NotFound)]
    [InlineData(DamlErrorCategory.InvalidGivenCurrentSystemStateSeekDifferentResource, StatusCode.FailedPrecondition)]
    [InlineData(DamlErrorCategory.BackgroundProcessDegradationWarning, StatusCode.Internal)]
    [InlineData(DamlErrorCategory.InternalUnsupportedOperation, StatusCode.Unimplemented)]
    public void ToGrpcStatusCode_maps_each_category_to_its_canonical_gRPC_status(
        DamlErrorCategory category,
        StatusCode expected)
    {
        category.ToGrpcStatusCode().Should().Be(expected);
    }

    [Fact]
    public void ToGrpcStatusCode_covers_every_defined_DamlErrorCategory_value()
    {
        foreach (var category in Enum.GetValues<DamlErrorCategory>())
        {
            var act = () => category.ToGrpcStatusCode();

            act.Should().NotThrow();
        }
    }

    [Fact]
    public void ToGrpcStatusCode_throws_for_an_undefined_category_value()
    {
        var act = () => ((DamlErrorCategory)999).ToGrpcStatusCode();

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AsGrpcStatusCode_returns_the_typed_status_of_a_gRPC_InfraError()
    {
        var infraError = new ExerciseOutcome<int>.InfraError((int)StatusCode.Unavailable, "participant unreachable");

        infraError.AsGrpcStatusCode().Should().Be(StatusCode.Unavailable);
    }

    [Fact]
    public void AsGrpcStatusCode_throws_for_an_InfraError_whose_status_is_not_a_gRPC_code()
    {
        var restInfraError = new ExerciseOutcome<int>.InfraError(503, "service unavailable");

        var act = () => restInfraError.AsGrpcStatusCode();

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("503");
    }

    [Fact]
    public void AsGrpcStatusCode_rejects_a_null_InfraError()
    {
        ExerciseOutcome<int>.InfraError infraError = null!;

        var act = () => infraError.AsGrpcStatusCode();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AsGrpcStatusCode_rejects_a_null_LedgerOperationException()
    {
        LedgerOperationException exception = null!;

        var act = () => exception.AsGrpcStatusCode();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AsGrpcStatusCode_returns_the_typed_status_of_an_infrastructure_LedgerOperationException()
    {
        var exception = new LedgerOperationException("boom", (int)StatusCode.DeadlineExceeded);

        exception.AsGrpcStatusCode().Should().Be(StatusCode.DeadlineExceeded);
    }

    [Fact]
    public void AsGrpcStatusCode_returns_null_for_a_LedgerOperationException_without_a_status()
    {
        var exception = new LedgerOperationException("no transport status");

        exception.AsGrpcStatusCode().Should().BeNull();
    }

    [Fact]
    public void AsGrpcStatusCode_throws_for_a_LedgerOperationException_whose_status_is_not_a_gRPC_code()
    {
        var exception = new LedgerOperationException("boom", 404);

        var act = () => exception.AsGrpcStatusCode();

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("404");
    }
}
