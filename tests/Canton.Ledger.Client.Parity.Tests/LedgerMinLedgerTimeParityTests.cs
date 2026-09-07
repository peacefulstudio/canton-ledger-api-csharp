// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Rest.Client;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// Pins that the <c>MinLedgerTime</c> bound a caller sets on a
/// <see cref="RuntimeCommands.CommandsSubmission"/> reaches the wire the same way on both
/// transports. Each transport encodes it differently — gRPC as a protobuf <c>Timestamp</c> or
/// <c>Duration</c>, REST as an ISO instant or the served document's duration string — so the
/// assertion is on the bound both encodings denote, not on their bytes. Both command builders are
/// <c>internal</c>; this project reaches them through <c>InternalsVisibleTo</c> from each client.
/// </summary>
public class LedgerMinLedgerTimeParityTests
{
    private static readonly Party Alice = new("party::alice");

    private static readonly DateTimeOffset LedgerTimeBound =
        new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    private sealed record ParityTemplate : ITemplate, IDamlRecord<ParityTemplate>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("pkg", "Module", "Template");
        public static string PackageId => "pkg";
        public static string PackageName => "pkg-name";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => new(TemplateId, []);

        public static ParityTemplate FromRecord(DamlRecord record) => new();
    }

    private static RuntimeCommands.CommandsSubmission Submission(RuntimeCommands.MinLedgerTime? bound)
    {
        var submission = RuntimeCommands.CommandsSubmission
            .Single(new RuntimeCommands.CreateCommand(ParityTemplate.TemplateId, new DamlRecord(null, [])))
            .WithActAs(Alice);

        return bound is null ? submission : submission.WithMinLedgerTime(bound);
    }

    private static Com.Daml.Ledger.Api.V2.Commands OverGrpc(RuntimeCommands.CommandsSubmission submission) =>
        new CommandBuilder(new LedgerClientOptions { GrpcAddress = "https://localhost:5001", UserId = "u" })
            .BuildCommands(submission);

    private static Rest.Client.Raw.Commands OverRest(RuntimeCommands.CommandsSubmission submission) =>
        RestCommandBuilder.BuildCommands(submission, userId: "u");

    [Fact]
    public void A_relative_bound_reaches_the_wire_as_the_same_delay_on_both_transports()
    {
        var submission = Submission(new RuntimeCommands.MinLedgerTime.Relative(TimeSpan.FromSeconds(5)));

        var overGrpc = OverGrpc(submission);
        var overRest = OverRest(submission);

        overGrpc.MinLedgerTimeRel.ToTimeSpan().Should().Be(TimeSpan.FromSeconds(5));
        overRest.MinLedgerTimeRel.Should().Be("5s");
        overGrpc.MinLedgerTimeAbs.Should().BeNull();
        overRest.MinLedgerTimeAbs.Should().BeNull();
    }

    [Fact]
    public void An_absolute_bound_reaches_the_wire_as_the_same_instant_on_both_transports()
    {
        var submission = Submission(new RuntimeCommands.MinLedgerTime.Absolute(LedgerTimeBound));

        var overGrpc = OverGrpc(submission);
        var overRest = OverRest(submission);

        overGrpc.MinLedgerTimeAbs.ToDateTimeOffset().Should().Be(LedgerTimeBound);
        overRest.MinLedgerTimeAbs.Should().Be(LedgerTimeBound);
        overGrpc.MinLedgerTimeRel.Should().BeNull();
        overRest.MinLedgerTimeRel.Should().BeNull();
    }

    [Fact]
    public void No_bound_leaves_both_wire_fields_unset_on_both_transports()
    {
        var submission = Submission(bound: null);

        var overGrpc = OverGrpc(submission);
        var overRest = OverRest(submission);

        overGrpc.MinLedgerTimeAbs.Should().BeNull();
        overGrpc.MinLedgerTimeRel.Should().BeNull();
        overRest.MinLedgerTimeAbs.Should().BeNull();
        overRest.MinLedgerTimeRel.Should().BeNull();
    }
}
