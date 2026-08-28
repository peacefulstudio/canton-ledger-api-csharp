// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Net;
using AwesomeAssertions;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Canton.Ledger.Abstractions.Tests;

public class DamlErrorCategoryExtensionsTests
{
    [Theory]
    [InlineData(DamlErrorCategory.Unknown, HttpStatusCode.InternalServerError)]
    [InlineData(DamlErrorCategory.TransientServerFailure, HttpStatusCode.ServiceUnavailable)]
    [InlineData(DamlErrorCategory.ContentionOnSharedResources, HttpStatusCode.Conflict)]
    [InlineData(DamlErrorCategory.DeadlineExceededRequestStateUnknown, HttpStatusCode.GatewayTimeout)]
    [InlineData(DamlErrorCategory.SystemInternalAssumptionViolated, HttpStatusCode.InternalServerError)]
    [InlineData(DamlErrorCategory.MaliciousOrFaultyBehaviour, HttpStatusCode.InternalServerError)]
    [InlineData(DamlErrorCategory.AuthInterceptorInvalidAuthenticationCredentials, HttpStatusCode.Unauthorized)]
    [InlineData(DamlErrorCategory.AuthorizationChecksFailed, HttpStatusCode.Forbidden)]
    [InlineData(DamlErrorCategory.InvalidIndependentOfSystemState, HttpStatusCode.BadRequest)]
    [InlineData(DamlErrorCategory.InvalidGivenCurrentSystemStateOther, HttpStatusCode.BadRequest)]
    [InlineData(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists, HttpStatusCode.Conflict)]
    [InlineData(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing, HttpStatusCode.NotFound)]
    [InlineData(DamlErrorCategory.InvalidGivenCurrentSystemStateSeekDifferentResource, HttpStatusCode.BadRequest)]
    [InlineData(DamlErrorCategory.BackgroundProcessDegradationWarning, HttpStatusCode.InternalServerError)]
    [InlineData(DamlErrorCategory.InternalUnsupportedOperation, HttpStatusCode.NotImplemented)]
    public void ToHttpStatusCode_maps_each_category_to_its_canonical_HTTP_status(
        DamlErrorCategory category,
        HttpStatusCode expected)
    {
        category.ToHttpStatusCode().Should().Be(expected);
    }

    [Fact]
    public void ToHttpStatusCode_covers_every_defined_DamlErrorCategory_value()
    {
        foreach (var category in Enum.GetValues<DamlErrorCategory>())
        {
            var act = () => category.ToHttpStatusCode();

            act.Should().NotThrow();
        }
    }

    [Fact]
    public void ToHttpStatusCode_throws_for_an_undefined_category_value()
    {
        var act = () => ((DamlErrorCategory)999).ToHttpStatusCode();

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
