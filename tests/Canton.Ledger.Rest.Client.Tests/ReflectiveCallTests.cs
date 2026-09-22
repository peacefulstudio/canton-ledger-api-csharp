// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public class ReflectiveCallTests
{
    private static object Failing() => throw new InvalidOperationException("static initialisation failed");

    private static object Succeeding() => "value";

    private static MethodInfo Method(string name) =>
        typeof(ReflectiveCallTests).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void Unwrapped_surfaces_the_cause_instead_of_the_TargetInvocationException()
    {
        FluentActions.Invoking(() => ReflectiveCall.Unwrapped(() => Method(nameof(Failing)).Invoke(null, null)))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("static initialisation failed");
    }

    [Fact]
    public void Unwrapped_returns_the_value_of_a_call_that_succeeds()
    {
        ReflectiveCall.Unwrapped(() => Method(nameof(Succeeding)).Invoke(null, null)).Should().Be("value");
    }
}
