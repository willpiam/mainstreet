using Xunit;
using Xunit.Abstractions;

namespace BankingCli.Tests;

[Trait("Category", "Live")]
[Collection("LiveApi")]
public sealed class LiveApiTransactionEdgeTests(ITestOutputHelper output) : LiveApiTestBase(output)
{
    [LiveTheory]
    [InlineData("Add one cent to savings.", "$0.01")]
    [InlineData("Please put .05 dollars in my savings.", "$0.05")]
    [InlineData("Top up my savngs by $12.30, please.", "$12.30")]
    [InlineData("Put $1,234.56 into savings.", "$1234.56")]
    [InlineData("Credit savings with one hundred and one dollars and one cent.", "$101.01")]
    [InlineData("deposit savings $2.50", "$2.50")]
    [InlineData("Increase my savings balance by 105 cents.", "$1.05")]
    public async Task DepositNormalizesDifferentExplicitAmountFormats(string request, string amount)
    {
        LiveRun result = await RunScenarioAsync([request]);

        Assert.Equal(new[] { RequestCall, ReplyCall }, result.Calls);
        Assert.Contains($"{amount} into savings.", result.Output);
        Assert.Contains($"You have {amount} in savings.", result.Output);
        Assert.Equal($"chequing=$0.00 | savings={amount}", result.Balance);
    }

    [LiveTheory]
    [InlineData("To savings, move $2.50 out of chequing.")]
    [InlineData("Take two dollars and fifty cents from checking and put it in savings.")]
    [InlineData("transfer chequing savings $2.50")]
    [InlineData("Move 2.50 between my accounts, with chequing as the source and savings as the destination.")]
    public async Task TransferPreservesDirectionAndBothAccountBalances(string request)
    {
        LiveRun result = await RunScenarioAsync(["deposit chequing 8.14", "deposit savings 6.03", request]);

        Assert.Equal(new[] { RequestCall, ReplyCall }, result.Calls);
        Assert.Contains("$2.50 from chequing to savings.", result.Output);
        Assert.Contains("You have $5.64 in chequing and $8.53 in savings.", result.Output);
        Assert.Equal("chequing=$5.64 | savings=$8.53", result.Balance);
    }

    [LiveFact]
    public async Task WithdrawingTheExactBalanceLeavesZeroInThatAccount()
    {
        LiveRun result = await RunScenarioAsync([
            "deposit chequing 3.17", "deposit savings 1.25", "withdraw savings $1.25"
        ]);

        Assert.Equal(new[] { RequestCall, ReplyCall }, result.Calls);
        Assert.Contains("$1.25 from savings.", result.Output);
        Assert.Contains("You have $0.00 in savings.", result.Output);
        Assert.Equal("chequing=$3.17 | savings=$0.00", result.Balance);
    }

    [LiveTheory]
    [InlineData("balance both")]
    [InlineData("balance all")]
    [InlineData("What do I have in chequing and in savings, respectively?")]
    public async Task ExplicitDualScopeReportsSeparateBalances(string request)
    {
        LiveRun result = await RunScenarioAsync(["deposit chequing 3.12", "deposit savings 7.46", request]);

        Assert.Equal(new[] { RequestCall, ReplyCall }, result.Calls);
        Assert.Contains("You have $3.12 in chequing and $7.46 in savings.", result.Output);
        Assert.Equal("chequing=$3.12 | savings=$7.46", result.Balance);
    }

    [LiveFact]
    public async Task BalanceRequestCanMentionAnAccountItExplicitlyExcludes()
    {
        LiveRun result = await RunScenarioAsync([
            "deposit chequing 3.12", "deposit savings 7.46",
            "Show me the savings balance only, without the chequing balance."
        ]);

        Assert.Equal(new[] { RequestCall, ReplyCall }, result.Calls);
        string reply = result.Output.TrimEnd().Split('\n')[^1];
        Assert.Contains("You have $7.46 in savings.", reply);
        Assert.DoesNotContain("chequing", reply);
        Assert.Equal("chequing=$3.12 | savings=$7.46", result.Balance);
    }

    [LiveFact]
    public async Task ConsecutiveOperationsSeeUpdatedBalancesAndPreserveCents()
    {
        LiveRun result = await RunScenarioAsync([
            "deposit chequing 2.03",
            "Put one cent into savings.",
            "Move exactly two dollars and three cents from checking into savings.",
            "Take seven cents out of savings.",
            "How much is left in each of my two accounts?",
            "balance"
        ]);

        Assert.Equal(new[] { RequestCall, ReplyCall, RequestCall, ReplyCall, RequestCall, ReplyCall, RequestCall, ReplyCall },
            result.Calls);
        Assert.Collection(result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries),
            line => Assert.Equal("DEPOSIT $2.03 TO chequing | chequing=$2.03", line),
            line => Assert.Contains("You have $0.01 in savings.", line),
            line => Assert.Contains("You have $0.00 in chequing and $2.04 in savings.", line),
            line => Assert.Contains("You have $1.97 in savings.", line),
            line => Assert.Contains("You have $0.00 in chequing and $1.97 in savings.", line),
            line => Assert.Equal("chequing=$0.00 | savings=$1.97", line));
        Assert.Equal("chequing=$0.00 | savings=$1.97", result.Balance);
    }

    [LiveFact]
    public async Task WithdrawalOneCentAboveTheBalanceReportsTheAvailableAmount()
    {
        LiveRun result = await RunScenarioAsync([
            "deposit chequing 8.14", "deposit savings 6.03", "Can I take $8.15 out of chequing now?"
        ]);

        Assert.Equal(new[] { RequestCall, ReplyCall }, result.Calls);
        Assert.Contains("withdraw $8.15 from chequing", result.Output);
        Assert.Contains("There aren't enough funds in chequing.", result.Output);
        Assert.Contains("You have $8.14 in chequing. No money was moved.", result.Output);
        Assert.Equal("chequing=$8.14 | savings=$6.03", result.Balance);
    }

    [LiveFact]
    public async Task TransferToAnAccountAtItsLimitKeepsTheSourceFunds()
    {
        const string maxBalance = "99999999999999999999999999.99";
        LiveRun result = await RunScenarioAsync([
            "deposit chequing 0.01", $"deposit savings {maxBalance}",
            "Please move one cent from chequing into savings."
        ]);

        Assert.Equal(new[] { RequestCall, ReplyCall }, result.Calls);
        Assert.Contains("transfer $0.01 from chequing to savings", result.Output);
        Assert.Contains("The transaction would put savings over its balance limit.", result.Output);
        Assert.Contains("No money was moved.", result.Output);
        Assert.Equal($"chequing=$0.01 | savings=${maxBalance}", result.Balance);
    }
}
