using Xunit;
using Xunit.Abstractions;

namespace BankingCli.Tests;

[Trait("Category", "Live")]
[Collection("LiveApi")]
public sealed class LiveApiApprovalEdgeTests(ITestOutputHelper output) : LiveApiTestBase(output)
{
    [LiveFact]
    public async Task AffirmativeInputExecutesThePreviewedDepositExactlyOnce()
    {
        LiveRun result = await RunScenarioAsync([
            "deposit chequing 3.00", "Please add one dollar and twenty cents to my savings.",
            "  YeS  ", "balance", "exit"
        ], approvalRequired: true, redirected: false);

        Assert.Equal(new[] { RequestCall, ReplyCall }, result.Calls);
        string[] lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length);
        Assert.StartsWith("bank> Deposit $1.20 into savings? [y/N] ", lines[1]);
        Assert.EndsWith("You have $1.20 in savings.", lines[1]);
        Assert.Equal("bank> chequing=$3.00 | savings=$1.20", lines[2]);
        Assert.Equal("bank> EXIT", lines[3]);
        Assert.Equal("chequing=$3.00 | savings=$1.20", result.Balance);
    }

    [LiveTheory]
    [InlineData("n")]
    [InlineData("yes, please")]
    [InlineData("withdraw savings 1")]
    public async Task OnlyUnqualifiedApprovalExecutesAndTheAnswerLineIsConsumed(string answer)
    {
        LiveRun result = await RunScenarioAsync([
            "deposit savings 6.10", "I'd like to take two dollars and ten cents out of savings.",
            answer, "balance", "exit"
        ], approvalRequired: true, redirected: false);

        Assert.Equal(new[] { RequestCall }, result.Calls);
        Assert.Contains("Withdraw $2.10 from savings? [y/N] No changes made.\n", result.Output);
        Assert.Contains("bank> chequing=$0.00 | savings=$6.10\nbank> EXIT\n", result.Output);
        Assert.Equal("chequing=$0.00 | savings=$6.10", result.Balance);
    }

    [LiveFact]
    public async Task EndOfInputDuringTransferApprovalCancelsAndSkipsTheReplyCall()
    {
        LiveRun result = await RunScenarioAsync([
            "deposit chequing 8.07", "Please send seven cents from my chequing to my savings."
        ], approvalRequired: true, redirected: false);

        Assert.Equal(new[] { RequestCall }, result.Calls);
        Assert.Contains("Transfer $0.07 from chequing to savings? [y/N] No changes made.\n", result.Output);
        Assert.Equal("chequing=$8.07 | savings=$0.00", result.Balance);
    }

    [LiveFact]
    public async Task PipedApprovalDoesNotApplyToAReadAfterASkippedWrite()
    {
        LiveRun result = await RunScenarioAsync([
            "deposit chequing 8.07", "Please send seven cents from my chequing to my savings.",
            "How much is left in checking?"
        ], approvalRequired: true);

        Assert.Equal(new[] { RequestCall, RequestCall, ReplyCall }, result.Calls);
        Assert.Contains("This request requires interactive confirmation. No changes made.\n", result.Output);
        Assert.Contains("You have $8.07 in chequing.", result.Output);
        Assert.DoesNotContain("[y/N]", result.Output);
        Assert.DoesNotContain("bank>", result.Output);
        Assert.Equal("chequing=$8.07 | savings=$0.00", result.Balance);
    }

    [LiveFact]
    public async Task ClarificationNeedsACompleteRestatementBeforeAnApprovalPrompt()
    {
        LiveRun result = await RunScenarioAsync([
            "deposit savings 6.10",
            "Could you put some money into savings?",
            "Two dollars, please.",
            "Please put two dollars into savings.", "y", "balance", "exit"
        ], approvalRequired: true, redirected: false);

        Assert.Equal(new[] { RequestCall, RequestCall, RequestCall, ReplyCall }, result.Calls);
        string[] lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(6, lines.Length);
        Assert.Contains("Please include a positive dollar amount", lines[1]);
        Assert.DoesNotContain("[y/N]", lines[2]);
        Assert.StartsWith("bank> Deposit $2.00 into savings? [y/N] ", lines[3]);
        Assert.EndsWith("You have $8.10 in savings.", lines[3]);
        Assert.Equal("chequing=$0.00 | savings=$8.10", result.Balance);
    }
}
