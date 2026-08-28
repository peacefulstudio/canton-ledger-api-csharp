// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Rest.Client.Raw;
using Daml.Runtime.Data;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Tests;

public class RestValueWireShapeTests
{
    [Fact]
    public async Task SubmitAndWait_writes_a_DamlParty_as_a_bare_string()
    {
        var choiceArgument = await SubmittedChoiceArgument(new DamlParty("alice::ns1"));

        choiceArgument.ValueKind.Should().Be(JsonValueKind.String);
        choiceArgument.GetString().Should().Be("alice::ns1");
    }

    [Fact]
    public async Task SubmitAndWait_writes_a_false_DamlBool_as_a_JSON_false_rather_than_omitting_it()
    {
        var choiceArgument = await SubmittedChoiceArgument(new DamlBool(false));

        choiceArgument.ValueKind.Should().Be(JsonValueKind.False);
    }

    [Fact]
    public async Task SubmitAndWait_writes_the_epoch_DamlDate_as_an_ISO_date_string()
    {
        var choiceArgument = await SubmittedChoiceArgument(DamlDate.FromDaysSinceEpoch(0));

        choiceArgument.ValueKind.Should().Be(JsonValueKind.String);
        choiceArgument.GetString().Should().Be("1970-01-01");
    }

    private static async Task<JsonElement> SubmittedChoiceArgument(DamlValue choiceArgument)
    {
        var (api, transport) = RestApiFactory.Build<ICommandServiceApi>();
        transport.WithResponse(HttpStatusCode.OK, """{"updateId":"u1","completionOffset":"1"}""");

        await api.SubmitAndWait(
            new Commands
            {
                CommandId = "c1",
                Commands1 =
                [
                    new Command
                    {
                        ExerciseCommand = new ExerciseCommand
                        {
                            ContractId = "00cid",
                            Choice = "Accept",
                            ChoiceArgument = RestValueEncoder.ToWireValue(choiceArgument),
                        },
                    },
                ],
            },
            TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        return body.RootElement
            .GetProperty("commands")[0]
            .GetProperty("ExerciseCommand")
            .GetProperty("choiceArgument")
            .Clone();
    }
}
