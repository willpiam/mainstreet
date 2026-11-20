using System.Text.Json;
using Xunit;

namespace BankingCli.Tests;

public sealed class RequestInterpreterEdgeTests
{
    private const string Failure = "I couldn't interpret that request. Please try again or type help for commands.";

    private const string Deposit = """
        {"kind":"operation","operation":"deposit","from":null,"to":"savings","amount":"4","scope":null,"message":null}
        """;

    public static TheoryData<string, string> InvalidRequests => new()
    {
        { "duplicate destination", Deposit.Replace("\"to\":\"savings\"", "\"to\":\"savings\",\"to\":\"chequing\"") },
        { "unexpected command", Deposit.Replace("\"message\":null", "\"message\":null,\"command\":\"deposit savings 50\"") },
        { "incorrect property casing", Deposit.Replace("\"to\":", "\"To\":") },
        { "missing nullable property", Deposit.Replace(",\"message\":null", "") },
        { "numeric amount instead of string", Deposit.Replace("\"4\"", "4") },
        { "array instead of object", $"[{Deposit}]" },
        { "operation with a clarification message", Deposit.Replace("\"message\":null", "\"message\":\"amount\"") },
        { "clarification with transaction fields", Deposit.Replace("\"kind\":\"operation\"", "\"kind\":\"clarify\"")
            .Replace("\"message\":null", "\"message\":\"amount\"") },
        { "deposit with source", Deposit.Replace("\"from\":null", "\"from\":\"chequing\"") },
        { "transaction with balance scope", Deposit.Replace("\"scope\":null", "\"scope\":\"both\"") },
        { "withdrawal with destination", Deposit.Replace("\"deposit\"", "\"withdraw\"")
            .Replace("\"from\":null", "\"from\":\"chequing\"") },
        { "transfer without source", Deposit.Replace("\"deposit\"", "\"transfer\"") },
        { "valid payload followed by more JSON", $"{Deposit} {Deposit}" },
        { "unsupported operation", Deposit.Replace("\"deposit\"", "\"exit\"") },
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task RejectsAmbiguousOrOutOfSchemaModelOutput(string scenario, string response)
    {
        var model = new ScriptedModel(response);

        Interpretation result = await new RequestInterpreter(model).InterpretAsync("Put four dollars in savings");

        Assert.True(result == new Interpretation(Message: Failure), $"{scenario}: {result}");
        Assert.Single(model.Calls);
    }

    [Theory]
    [InlineData("4\u0000")]
    [InlineData("4\nbalance")]
    [InlineData("４.００")]
    public async Task ModelAmountCannotContainControlCharactersOrNonAsciiDigits(string amount)
    {
        string response = Deposit.Replace("\"4\"", JsonSerializer.Serialize(amount));

        Interpretation result = await new RequestInterpreter(new ScriptedModel(response)).InterpretAsync("a deposit");

        Assert.Equal(new Interpretation(Message: Failure), result);
    }

    [Fact]
    public async Task CommandAndPreviewAgreeForAnAmountWithLeadingZeros()
    {
        string response = Deposit.Replace("\"4\"", "\"00004.50\"");

        Interpretation result = await new RequestInterpreter(new ScriptedModel(response)).InterpretAsync("a deposit");

        Assert.Equal(new Interpretation("deposit savings 4.5", "Deposit $4.50 into savings"), result);
    }
}

public sealed class ReplyWriterEdgeTests
{
    [Theory]
    [InlineData("{\"wording\":\"direct_decline\",\"wording\":\"explanatory_decline\"}")]
    [InlineData("{\"Wording\":\"direct_decline\"}")]
    [InlineData("{\"wording\":null}")]
    [InlineData("[{\"wording\":\"direct_decline\"}]")]
    [InlineData("{\"wording\":\"explanatory\"}")]
    [InlineData("{\"wording\":\"I deposited $50.00 into savings.\"}")]
    public async Task InvalidOrMisleadingWordingPreservesTheOriginalDecline(string response)
    {
        var bank = new BankSession();
        bank.Execute("deposit savings 2.50");
        CommandResult decline = bank.Execute("withdraw savings 2.51");
        var model = new ScriptedModel(response);

        string output = await new ReplyWriter(model).WriteAsync(decline);

        Assert.Equal("ERROR INSUFFICIENT_FUNDS | savings=$2.50", output);
        Assert.Single(model.Calls);
        Assert.Equal("chequing=$0.00 | savings=$2.50", bank.Execute("balance").Output);
    }

    [Fact]
    public async Task MissingSimulatorFactsBypassTheModel()
    {
        CommandResult result = new BankSession().Execute("deposit savings 1.005");
        var model = new ScriptedModel();

        Assert.Equal("ERROR INVALID_AMOUNT", await new ReplyWriter(model).WriteAsync(result));
        Assert.Empty(model.Calls);
    }
}
