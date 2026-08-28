// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public class LedgerResultTooLargeExceptionTests
{
    [Fact]
    public void Constructor_carries_the_message()
    {
        var exception = new LedgerResultTooLargeException("too large");

        exception.Message.Should().Be("too large");
        exception.Should().BeAssignableTo<Exception>();
    }

    [Fact]
    public void Constructor_carries_the_message_and_inner_exception()
    {
        var inner = new InvalidOperationException("inner");

        var exception = new LedgerResultTooLargeException("too large", inner);

        exception.Message.Should().Be("too large");
        exception.InnerException.Should().BeSameAs(inner);
    }
}
