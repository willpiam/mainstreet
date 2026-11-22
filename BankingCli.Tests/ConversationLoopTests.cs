using System.Text.Json;
using Xunit;

namespace BankingCli.Tests;

public sealed class ConversationLoopTests
{
    [Fact]
    public async Task DirectCommandsKeepRawOutputAndBypassTheModel()
    {
        var model = new ScriptedModel();
        string output = await RunAsync(model, [
            "HELP", "?", " ", "   DePoSiT\tSavings 10 ", "transfer savings chequing 4",
            "withdraw chequing 1", "BALANCE", "exit", "deposit savings 1"
        ], approval: true);

        Assert.Equal(2, output.Split("ACCOUNTS chequing | savings").Length - 1);
        Assert.Contains("DEPOSIT $10.00 TO savings | savings=$10.00", output);
        Assert.Contains("TRANSFER $4.00 FROM savings TO chequing | savings=$6.00 | chequing=$4.00", output);
        Assert.Contains("WITHDRAW $1.00 FROM chequing | chequing=$3.00", output);
        Assert.Contains("chequing=$3.00 | savings=$6.00", output);
        Assert.EndsWith("EXIT\n", output);
        Assert.DoesNotContain("bank>", output);
        Assert.Empty(model.Calls);
    }

    [Fact]
    public async Task WithoutAModelDirectCommandsStillWork()
    {
        string output = await RunAsync(null, ["Show me both balances", "deposit savings 0", "balance savings"]);
        Assert.Equal("AI is not configured. Type help for commands.\nERROR INVALID_AMOUNT\nsavings=$0.00\n", output);
    }

    [Theory]
    [InlineData("help extra", "ERROR INVALID_ARGUMENTS")]
    [InlineData("exit extra", "ERROR INVALID_ARGUMENTS")]
    [InlineData("balance checking", "ERROR INVALID_ACCOUNT")]
    [InlineData("deposit investment 5", "ERROR INVALID_ACCOUNT")]
    [InlineData("withdraw savings invalid", "ERROR INVALID_AMOUNT")]
    [InlineData("transfer savings investment 5", "ERROR INVALID_ACCOUNT")]
    [InlineData("transfer savings chequing", "ERROR INVALID_ARGUMENTS")]
    public async Task InvalidDirectCommandsReportSimulatorErrorsWithoutCallingTheModel(string input, string error)
    {
        var model = new ScriptedModel();
        string output = await RunAsync(model, [input, "balance", "exit"], approval: true);

        Assert.Equal($"{error}\nchequing=$0.00 | savings=$0.00\nEXIT\n", output);
        Assert.Empty(model.Calls);
    }

    [Fact]
    public async Task NaturalLanguageSupportsAllOperationsAndBalanceScopes()
    {
        var model = new ScriptedModel(
            Request("deposit", to: "savings", amount: "10"), Reply("concise"),
            Request("transfer", from: "savings", to: "chequing", amount: "4"), Reply("concise"),
            Request("withdraw", from: "chequing", amount: "1.50"), Reply("explanatory"),
            Request("balance", scope: "savings"), Reply("concise"),
            Request("balance", scope: "chequing"), Reply("concise"),
            Request("balance", scope: "both"), Reply("explanatory"));

        string output = await RunAsync(model, [
            "Deposit $10 into savings", "Transfer four dollars from savings to chequing",
            "Withdraw $1.50 from chequing", "How much is in savings?", "How much is in chequing?",
            "Balance both"
        ]);

        Assert.Equal("""
            I deposited $10.00 into savings. You have $10.00 in savings.
            I transferred $4.00 from savings to chequing. You have $6.00 in savings and $4.00 in chequing.
            I've completed your request to withdraw $1.50 from chequing. You have $2.50 in chequing.
            You have $6.00 in savings.
            You have $2.50 in chequing.
            Here's what you have available. You have $2.50 in chequing and $6.00 in savings.
            """ + "\n", output);
        Assert.All(model.Calls.Chunk(2), calls =>
        {
            Assert.Equal("banking_request", calls[0].Schema);
            Assert.Equal("banking_reply", calls[1].Schema);
        });
    }

    [Theory]
    [InlineData("Transfer $4 out of savings", "clarify", "direction", "source and destination")]
    [InlineData("Transfer from savings 10", "clarify", "direction", "source and destination")]
    [InlineData("Transfer savings to chequing", "clarify", "amount", "positive dollar amount")]
    [InlineData("Put some money into savings", "clarify", "amount", "positive dollar amount")]
    [InlineData("What is my balance?", "clarify", "account", "account name")]
    [InlineData("Transfer a dollar 15 from savings to checking", "clarify", "amount", "positive dollar amount")]
    [InlineData("Deposit $4 in savings and withdraw $2 from chequing", "unsupported", "one_operation", "one banking operation")]
    public async Task ClarificationLeavesBothBalancesUnchanged(string input, string kind, string reason, string expected)
    {
        var model = new ScriptedModel(Request(kind: kind, message: reason));
        string output = await RunAsync(model, ["deposit savings 10", input, "balance"]);

        Assert.Contains(expected, output);
        Assert.EndsWith("chequing=$0.00 | savings=$10.00\n", output);
        Assert.Single(model.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AmbiguousRequestAsksForClarificationBeforeAnyApproval(bool approval)
    {
        var model = new ScriptedModel(Request(kind: "clarify", message: "amount"));
        string output = await RunAsync(model,
            ["deposit savings 20", "Transfer a dollar 15 from savings to checking", "balance", "exit"],
            approval: approval, redirected: false);

        Assert.Contains("Please include a positive dollar amount", output);
        Assert.DoesNotContain("[y/N]", output);
        Assert.Contains("chequing=$0.00 | savings=$20.00\n", output);
        Assert.EndsWith("bank> EXIT\n", output);
        Assert.Single(model.Calls);
    }

    [Theory]
    [InlineData("yes", true)]
    [InlineData(" Y ", true)]
    [InlineData("no", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public async Task InteractiveApprovalExecutesOnlyWhenConfirmed(string? answer, bool approved)
    {
        string intent = Request("transfer", from: "savings", to: "chequing", amount: "4");
        var model = approved ? new ScriptedModel(intent, Reply("concise")) : new ScriptedModel(intent);
        var bank = new BankSession();

        string output = await RunAsync(model, ["deposit savings 10", "Move $4 from savings to chequing", answer],
            approval: true, redirected: false, bank: bank);

        Assert.Contains("Transfer $4.00 from savings to chequing? [y/N]", output);
        Assert.Contains(approved ? "I transferred $4.00" : "No changes made.", output);
        Assert.Equal(approved ? "chequing=$4.00 | savings=$6.00" : "chequing=$0.00 | savings=$10.00",
            bank.Execute("balance").Output);
    }

    [Fact]
    public async Task RedirectedApprovalSkipsTheTransactionAndPreservesFollowingLines()
    {
        var model = new ScriptedModel(
            Request("transfer", from: "savings", to: "chequing", amount: "4"),
            Request("balance", scope: "both"), Reply("concise"));

        string output = await RunAsync(model, [
            "deposit savings 10", "Move $4 from savings to chequing", "balance", "Show both balances"
        ], approval: true);

        Assert.Contains("requires interactive confirmation. No changes made.", output);
        Assert.Contains("chequing=$0.00 | savings=$10.00", output);
        Assert.Contains("You have $0.00 in chequing and $10.00 in savings.", output);
        Assert.DoesNotContain("[y/N]", output);
    }

    [Fact]
    public async Task DeclineReplyUsesTheSimulatorReasonAndCorrectAccount()
    {
        var model = new ScriptedModel(
            Request("transfer", from: "savings", to: "chequing", amount: "20"), Reply("direct_decline"));

        string output = await RunAsync(model, ["deposit savings 10", "Transfer $20 from savings to chequing", "balance"]);
        Assert.Contains("I couldn't transfer $20.00 from savings to chequing. There aren't enough funds in savings. "
            + "You have $10.00 in savings. No money was moved.", output);
        Assert.EndsWith("chequing=$0.00 | savings=$10.00\n", output);

        using JsonDocument facts = JsonDocument.Parse(model.Calls[1].Input);
        Assert.Equal("INSUFFICIENT_FUNDS", facts.RootElement.GetProperty("error").GetString());
        Assert.False(facts.RootElement.GetProperty("succeeded").GetBoolean());
    }

    [Theory]
    [InlineData(null)] // Timeout or provider failure is normalized by the adapter.
    [InlineData("{invalid")]
    [InlineData("{}")]
    [InlineData("{\"kind\":\"operation\",\"operation\":\"deposit\",\"from\":null,\"to\":\"investment\",\"amount\":\"4\",\"scope\":null,\"message\":null}")]
    [InlineData("{\"kind\":\"operation\",\"operation\":\"transfer\",\"from\":\"savings\",\"to\":\"chequing\",\"amount\":\"4.001\",\"scope\":null,\"message\":null}")]
    [InlineData("{\"kind\":\"operation\",\"operation\":\"balance\",\"from\":null,\"to\":null,\"amount\":null,\"scope\":null,\"message\":null}")]
    public async Task InterpretationFailureNeverExecutes(string? response)
    {
        var model = new ScriptedModel(response);
        string output = await RunAsync(model, ["deposit savings 10", "Move $4 from savings to chequing", "balance"]);

        Assert.Contains("I couldn't interpret", output);
        Assert.EndsWith("chequing=$0.00 | savings=$10.00\n", output);
        Assert.Single(model.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{invalid")]
    [InlineData("{\"wording\":\"direct_decline\"}")]
    [InlineData("{\"wording\":\"concise\",\"balance\":100}")]
    public async Task ReplyFailureShowsRawResultWithoutRunningTransferAgain(string? reply)
    {
        var model = new ScriptedModel(Request("transfer", from: "savings", to: "chequing", amount: "4"), reply);
        string output = await RunAsync(model, ["deposit savings 10", "Move $4 from savings to chequing", "balance"]);

        Assert.Contains("TRANSFER $4.00 FROM savings TO chequing | savings=$6.00 | chequing=$4.00", output);
        Assert.EndsWith("chequing=$4.00 | savings=$6.00\n", output);
        AssertReplyFacts(model);
    }

    [Fact]
    public async Task GeneratedSummaryPrintsTheModelReply()
    {
        const string reply = "I moved $4.00 from savings to chequing. You now have $6.00 in savings and $4.00 in chequing.";
        var model = new ScriptedModel(
            Request("transfer", from: "savings", to: "chequing", amount: "4"),
            JsonSerializer.Serialize(new { reply }));

        string output = await RunAsync(model, ["deposit savings 10", "Move four dollars from savings to chequing", "balance"],
            summary: SummarySetting.Generated);

        Assert.Contains(reply, output);
        Assert.DoesNotContain("TRANSFER $", output);
        Assert.EndsWith("chequing=$4.00 | savings=$6.00\n", output);
        Assert.Contains("\"reply\"", model.Calls[1].SchemaDocument);
        AssertReplyFacts(model);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{invalid")]
    [InlineData("{\"reply\":\"\"}")]
    [InlineData("{\"reply\":\"   \"}")]
    [InlineData("{\"wording\":\"concise\"}")]
    [InlineData("{\"reply\":\"I moved $4.00.\",\"extra\":true}")]
    public async Task GeneratedSummaryFailureShowsRawResultWithoutRunningTransferAgain(string? reply)
    {
        var model = new ScriptedModel(Request("transfer", from: "savings", to: "chequing", amount: "4"), reply);
        string output = await RunAsync(model, ["deposit savings 10", "Move four dollars from savings to chequing", "balance"],
            summary: SummarySetting.Generated);

        Assert.Contains("TRANSFER $4.00 FROM savings TO chequing | savings=$6.00 | chequing=$4.00", output);
        Assert.DoesNotContain("I moved $4.00.", output);
        Assert.EndsWith("chequing=$4.00 | savings=$6.00\n", output);
        AssertReplyFacts(model);
    }

    private static void AssertReplyFacts(ScriptedModel model)
    {
        using JsonDocument facts = JsonDocument.Parse(model.Calls[1].Input);
        JsonElement balances = facts.RootElement.GetProperty("balances");
        Assert.Equal("savings", balances[0].GetProperty("account").GetString());
        Assert.Equal(6m, balances[0].GetProperty("amount").GetDecimal());
        Assert.Equal("chequing", balances[1].GetProperty("account").GetString());
        Assert.Equal(4m, balances[1].GetProperty("amount").GetDecimal());
    }

    private static string Request(string? operation = null, string? from = null, string? to = null,
        string? amount = null, string? scope = null, string kind = "operation", string? message = null) =>
        JsonSerializer.Serialize(new { kind, operation, from, to, amount, scope, message });

    private static string Reply(string wording) => JsonSerializer.Serialize(new { wording });

    private static async Task<string> RunAsync(ScriptedModel? model, string?[] input, bool approval = false,
        bool redirected = true, BankSession? bank = null, SummarySetting summary = SummarySetting.Safe)
    {
        var console = new ScriptedConsole(input, redirected);
        await new ConversationLoop(bank ?? new BankSession(), console, model, approval, summary).RunAsync();
        if (model is not null)
        {
            Assert.Empty(model.Responses);
        }

        return console.Output.ToString();
    }
}
