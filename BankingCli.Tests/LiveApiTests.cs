using BankingCli;
using BankingCli.Ai;
using Xunit;
using Xunit.Abstractions;

namespace BankingCli.Tests;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute() => Timeout = 120_000;

    public override string? Skip
    {
        get => LiveConfiguration.SkipReason;
        set { }
    }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class LiveTheoryAttribute : TheoryAttribute
{
    public LiveTheoryAttribute() => Timeout = 120_000;

    public override string? Skip
    {
        get => LiveConfiguration.SkipReason;
        set { }
    }
}

[CollectionDefinition("LiveApi", DisableParallelization = true)]
public sealed class LiveApiCollection;

internal static class LiveConfiguration
{
    static LiveConfiguration()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BankingCli.sln")))
        {
            directory = directory.Parent;
        }

        Root = directory?.FullName;
        if (Root is null)
        {
            SkipReason = "Live tests could not find the repository root.";
            Config = new AppConfig(null, null, null, null, false);
            return;
        }

        try
        {
            Config = AppConfig.LoadFrom(Root, environmentOverrides: false);
        }
        catch (Exception exception)
        {
            SkipReason = "Live tests could not read .env: " + exception.Message;
            Config = new AppConfig(null, null, null, null, false);
            return;
        }

        if (string.IsNullOrWhiteSpace(Config.ApiKey) || string.IsNullOrWhiteSpace(Config.Model))
        {
            SkipReason = "Repository .env must set OPENAI_API_KEY and OPENAI_MODEL.";
        }
    }

    public static string? Root { get; }
    public static string? SkipReason { get; }
    public static AppConfig Config { get; }
}

public abstract class LiveApiTestBase(ITestOutputHelper output)
{
    protected const string RequestCall = "banking_request";
    protected const string ReplyCall = "banking_reply";

    protected async Task<(string Output, string Balance)> RunAsync(params string[] lines)
    {
        LiveRun result = await RunScenarioAsync(lines);
        return (result.Output, result.Balance);
    }

    protected async Task<LiveRun> RunScenarioAsync(string[] lines, bool approvalRequired = false, bool redirected = true)
    {
        AppConfig config = LiveConfiguration.Config;
        output.WriteLine($"model={config.Model}");
        output.WriteLine($"reasoning={(string.IsNullOrWhiteSpace(config.Reasoning) ? "(unset)" : config.Reasoning)}");
        IChatModel? model = OpenAiChatModel.Create(config);
        Assert.NotNull(model);

        var observed = new ObservingModel(model);
        var bank = new BankSession();
        var console = new ScriptedConsole(lines, redirected);
        await new ConversationLoop(bank, console, observed, approvalRequired).RunAsync();
        string transcript = console.Output.ToString();
        output.WriteLine(transcript);
        Assert.All(observed.Calls, call => Assert.False(string.IsNullOrWhiteSpace(call.Response),
            $"The configured provider returned no completion for {call.Schema}."));
        return new LiveRun(transcript, bank.Execute("balance").Output, observed.Calls.Select(call => call.Schema).ToArray());
    }

    protected sealed record LiveRun(string Output, string Balance, string[] Calls);

    private sealed class ObservingModel(IChatModel inner) : IChatModel
    {
        public List<(string Schema, string? Response)> Calls { get; } = [];

        public async Task<string?> CompleteAsync(string instructions, string input, string schemaName, string schema)
        {
            string? response = await inner.CompleteAsync(instructions, input, schemaName, schema);
            Calls.Add((schemaName, response));
            return response;
        }
    }
}

[Trait("Category", "Live")]
[Collection("LiveApi")]
public sealed class LiveApiSuccessTests(ITestOutputHelper output) : LiveApiTestBase(output)
{
    [LiveFact]
    public async Task UsesTheModelAndReasoningFromTheEnvFile()
    {
        AppConfig config = LiveConfiguration.Config;
        string env = File.ReadAllText(Path.Combine(LiveConfiguration.Root!, ".env"));
        Assert.False(string.IsNullOrWhiteSpace(config.Model));
        Assert.Contains($"OPENAI_MODEL=\"{config.Model}\"", env);
        Assert.False(string.IsNullOrWhiteSpace(config.Reasoning));
        Assert.Contains($"OPENAI_REASONING=\"{config.Reasoning}\"", env);
        Assert.NotNull(OpenAiChatModel.Create(config));
        await Task.CompletedTask;
    }

    [LiveFact]
    public async Task DirectCommandsDoNotCallTheConfiguredModel()
    {
        LiveRun result = await RunScenarioAsync(["deposit savings 10", "balance", "exit"]);

        Assert.Empty(result.Calls);
        Assert.Contains("DEPOSIT $10.00 TO savings | savings=$10.00", result.Output);
        Assert.Contains("chequing=$0.00 | savings=$10.00", result.Output);
        Assert.Contains("EXIT", result.Output);
    }

    [LiveFact]
    public async Task DepositInPlainEnglishCreditsSavings()
    {
        var (transcript, balance) = await RunAsync("Deposit ten dollars into savings");
        Assert.Contains("You have $10.00 in savings.", transcript);
        Assert.DoesNotContain("DEPOSIT $", transcript);
        Assert.Equal("chequing=$0.00 | savings=$10.00", balance);
    }

    [LiveFact]
    public async Task TransferInPlainEnglishMovesTheRequestedAmount()
    {
        var (transcript, balance) = await RunAsync(
            "deposit savings 10", "Transfer four dollars from savings to chequing");
        Assert.Contains("You have $6.00 in savings and $4.00 in chequing.", transcript);
        Assert.DoesNotContain("TRANSFER $", transcript);
        Assert.Equal("chequing=$4.00 | savings=$6.00", balance);
    }

    [LiveTheory]
    [InlineData("Transfer $1.15 from savings to chequing")]
    [InlineData("Transfer one dollar and fifteen cents from savings to chequing")]
    [InlineData("Transfer a dollar fifteen cents from savings to chequing")]
    public async Task UnambiguousCentAmountsTransferExactly(string request)
    {
        var (_, balance) = await RunAsync("deposit savings 10", request);
        Assert.Equal("chequing=$1.15 | savings=$8.85", balance);
    }

    [LiveFact]
    public async Task WithdrawalInPlainEnglishDebitsChequing()
    {
        var (transcript, balance) = await RunAsync(
            "deposit chequing 5", "Withdraw a dollar from chequing");
        Assert.Contains("You have $4.00 in chequing.", transcript);
        Assert.DoesNotContain("WITHDRAW $", transcript);
        Assert.Equal("chequing=$4.00 | savings=$0.00", balance);
    }

    [LiveTheory]
    [InlineData("checking")]
    [InlineData("checquing")]
    public async Task OtherSpellingsOfChequingAreTreatedAsChequing(string account)
    {
        var (transcript, balance) = await RunAsync($"Deposit ten dollars into {account}");
        Assert.Contains("You have $10.00 in chequing.", transcript);
        Assert.Equal("chequing=$10.00 | savings=$0.00", balance);
    }

    [LiveFact]
    public async Task SavingsBalanceMatchesTheSimulator()
    {
        var (transcript, balance) = await RunAsync("deposit savings 10", "How much is in savings?");
        Assert.Contains("You have $10.00 in savings.", transcript);
        Assert.DoesNotContain("chequing", transcript);
        Assert.Equal("chequing=$0.00 | savings=$10.00", balance);
    }

    [LiveFact]
    public async Task BothBalancesMatchTheSimulator()
    {
        var (transcript, balance) = await RunAsync("deposit savings 10", "Show both account balances");
        Assert.Contains("You have $0.00 in chequing and $10.00 in savings.", transcript);
        Assert.Equal("chequing=$0.00 | savings=$10.00", balance);
    }
}

[Trait("Category", "Live")]
[Collection("LiveApi")]
public sealed class LiveApiFailureTests(ITestOutputHelper output) : LiveApiTestBase(output)
{
    [LiveTheory]
    [InlineData("Transfer four dollars out of savings", "Please include both the source and destination accounts and try again.")]
    [InlineData("Put some money into savings", "Please include a positive dollar amount with up to two decimal places and try again.")]
    [InlineData("What is my balance?", "Please include the account name, or say both accounts for a balance check, and try again.")]
    [InlineData("Deposit $4 in savings and withdraw $2 from chequing", "Please submit one banking operation at a time.")]
    public async Task IncompleteRequestDoesNotMoveMoney(string request, string expected)
    {
        var (transcript, balance) = await RunAsync("deposit savings 10", request);
        Assert.Contains(expected, transcript);
        Assert.Equal("chequing=$0.00 | savings=$10.00", balance);
    }

    [LiveFact]
    public async Task MoveFromSavingsWithoutADestinationDoesNotMoveMoney()
    {
        var (transcript, balance) = await RunAsync("deposit savings 10", "Move four dollars from savings");
        string reply = LastReply(transcript);
        Assert.True(
            reply is "Please include both the source and destination accounts and try again."
                or "Please include the account name, or say both accounts for a balance check, and try again.",
            reply);
        Assert.Equal("chequing=$0.00 | savings=$10.00", balance);
    }

    [LiveTheory]
    [InlineData("Transfer a dollar 15 from savings to chequing")]
    [InlineData("Transfer a dollar fifteen from savings to chequing")]
    [InlineData("Withdraw five 20 from savings")]
    public async Task AmbiguousAmountAsksForClarification(string request)
    {
        var (transcript, balance) = await RunAsync("deposit savings 10", request);
        Assert.Contains("Please include a positive dollar amount with up to two decimal places and try again.",
            transcript);
        Assert.Equal("chequing=$0.00 | savings=$10.00", balance);
    }

    [LiveFact]
    public async Task AmbiguousDollarAmountAsksForClarification()
    {
        var (transcript, balance) = await RunAsync(
            "deposit chequing 100", "transfer a dollar 15 from chequing to savings");
        Assert.Equal("Please include a positive dollar amount with up to two decimal places and try again.",
            LastReply(transcript));
        Assert.Equal("chequing=$100.00 | savings=$0.00", balance);
    }

    [LiveFact]
    public async Task TransferAboveTheBalanceIsDeclined()
    {
        var (transcript, balance) = await RunAsync(
            "deposit savings 10", "Transfer $20 from savings to chequing");
        Assert.Contains("There aren't enough funds in savings.", transcript);
        Assert.Contains("No money was moved.", transcript);
        Assert.Equal("chequing=$0.00 | savings=$10.00", balance);
    }

    [LiveFact]
    public async Task TransferToTheSameAccountDoesNotMoveMoney()
    {
        var (transcript, balance) = await RunAsync(
            "deposit savings 10", "Transfer four dollars from savings to savings");
        string reply = LastReply(transcript);
        Assert.True(
            reply.Contains("A transfer needs two different accounts.", StringComparison.Ordinal)
            || IsSafeRefusal(reply),
            reply);
        Assert.DoesNotContain("I transferred", transcript);
        Assert.DoesNotContain("I've completed", transcript);
        Assert.Equal("chequing=$0.00 | savings=$10.00", balance);
    }

    [LiveFact]
    public async Task UnknownAccountIsNotSubstituted()
    {
        var (transcript, balance) = await RunAsync("deposit savings 10", "Deposit four dollars into brokerage");
        AssertStoppedWithoutMovingMoney(transcript);
        Assert.Equal("chequing=$0.00 | savings=$10.00", balance);
    }

    [LiveFact]
    public async Task UnrelatedMessageDoesNotMoveMoney()
    {
        var (transcript, balance) = await RunAsync("deposit savings 10", "Tell me a joke");
        AssertStoppedWithoutMovingMoney(transcript);
        Assert.Equal("chequing=$0.00 | savings=$10.00", balance);
    }

    private static void AssertStoppedWithoutMovingMoney(string transcript) =>
        Assert.True(IsSafeRefusal(LastReply(transcript)), LastReply(transcript));

    private static bool IsSafeRefusal(string reply) => reply is
        "Please include the account name, or say both accounts for a balance check, and try again."
        or "Please include a positive dollar amount with up to two decimal places and try again."
        or "Please include both the source and destination accounts and try again."
        or "Please submit one banking operation at a time."
        or "Please try again with one complete deposit, withdrawal, transfer, or balance request."
        or "I couldn't interpret that request. Please try again or type help for commands.";

    private static string LastReply(string transcript) =>
        transcript.Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1];
}
