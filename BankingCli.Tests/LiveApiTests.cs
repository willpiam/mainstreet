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
    protected async Task<(string Output, string Balance)> RunAsync(params string[] lines)
    {
        AppConfig config = LiveConfiguration.Config;
        output.WriteLine($"model={config.Model}");
        output.WriteLine($"reasoning={(string.IsNullOrWhiteSpace(config.Reasoning) ? "(unset)" : config.Reasoning)}");
        IChatModel? model = OpenAiChatModel.Create(config);
        Assert.NotNull(model);

        var bank = new BankSession();
        var console = new ScriptedConsole(lines);
        await new ConversationLoop(bank, console, model, approvalRequired: false).RunAsync();
        string transcript = console.Output.ToString();
        output.WriteLine(transcript);
        return (transcript, bank.Execute("balance").Output);
    }

    private sealed class ScriptedConsole(params string[] input) : IConsoleIO
    {
        private readonly Queue<string?> lines = new(input);
        public StringWriter Output { get; } = new() { NewLine = "\n" };
        public bool IsInputRedirected => true;
        public string? ReadLine() => lines.TryDequeue(out string? line) ? line : null;
        public void Write(string text) => Output.Write(text);
        public void WriteLine(string text) => Output.WriteLine(text);
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
        var observed = new ObservingModel(OpenAiChatModel.Create(LiveConfiguration.Config)!);
        var console = new DirectConsole("deposit savings 10", "balance", "exit");
        await new ConversationLoop(new BankSession(), console, observed, approvalRequired: false).RunAsync();

        Assert.Equal(0, observed.Calls);
        Assert.Contains("DEPOSIT $10.00 TO savings | savings=$10.00", console.Output.ToString());
        Assert.Contains("chequing=$0.00 | savings=$10.00", console.Output.ToString());
        Assert.Contains("EXIT", console.Output.ToString());
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

    [LiveFact]
    public async Task WithdrawalInPlainEnglishDebitsChequing()
    {
        var (transcript, balance) = await RunAsync(
            "deposit chequing 5", "Withdraw a dollar from chequing");
        Assert.Contains("You have $4.00 in chequing.", transcript);
        Assert.DoesNotContain("WITHDRAW $", transcript);
        Assert.Equal("chequing=$4.00 | savings=$0.00", balance);
    }

    [LiveFact]
    public async Task CheckingIsTreatedAsChequing()
    {
        var (transcript, balance) = await RunAsync("Deposit ten dollars into checking");
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

    private sealed class ObservingModel(IChatModel inner) : IChatModel
    {
        public int Calls { get; private set; }

        public Task<string?> CompleteAsync(string instructions, string input, string schemaName, string schema)
        {
            Calls++;
            return inner.CompleteAsync(instructions, input, schemaName, schema);
        }
    }

    private sealed class DirectConsole(params string[] input) : IConsoleIO
    {
        private readonly Queue<string?> lines = new(input);
        public StringWriter Output { get; } = new() { NewLine = "\n" };
        public bool IsInputRedirected => true;
        public string? ReadLine() => lines.TryDequeue(out string? line) ? line : null;
        public void Write(string text) => Output.Write(text);
        public void WriteLine(string text) => Output.WriteLine(text);
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

    [LiveFact]
    public async Task AmbiguousDollarAmountAsksForClarification()
    {
        var (transcript, balance) = await RunAsync(
            "deposit chequing 100", "transfer a dollar 15 from checking to savings");
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
