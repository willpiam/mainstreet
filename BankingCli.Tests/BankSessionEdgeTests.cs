using System.Globalization;
using Xunit;

namespace BankingCli.Tests;

public sealed class BankSessionEdgeTests
{
    private const string Maximum = "99999999999999999999999999.99";

    [Theory]
    [InlineData("-0.01")]
    [InlineData("+0.01")]
    [InlineData("1.000")]
    [InlineData("1.")]
    [InlineData("0.00")]
    [InlineData("1,25")]
    [InlineData("1,000.00")]
    [InlineData("1e2")]
    [InlineData("١.٢٥")]
    [InlineData("100000000000000000000000000.00")]
    [InlineData("79228162514264337593543950336")]
    public void RejectedAmountLeavesBothBalancesUntouched(string amount)
    {
        var bank = new BankSession();
        bank.Execute("deposit chequing 4.57");
        bank.Execute("deposit savings 1.23");

        CommandResult result = bank.Execute($"transfer chequing savings {amount}");

        Assert.Equal("ERROR INVALID_AMOUNT", result.Output);
        Assert.Null(result.Facts);
        Assert.Equal("chequing=$4.57 | savings=$1.23", bank.Execute("balance").Output);
    }

    [Fact]
    public void BothAccountsCanReachTheirLimitsWithoutLosingCentPrecision()
    {
        var bank = new BankSession();
        Assert.StartsWith("DEPOSIT ", bank.Execute($"deposit chequing {Maximum}").Output);
        Assert.StartsWith("DEPOSIT ", bank.Execute("deposit savings 99999999999999999999999999.98").Output);

        CommandResult transfer = bank.Execute("transfer chequing savings 0.01");
        Assert.Equal(
            $"TRANSFER $0.01 FROM chequing TO savings | chequing=$99999999999999999999999999.98 | savings=${Maximum}",
            transfer.Output);
        Assert.StartsWith("DEPOSIT ", bank.Execute("deposit chequing 0.01").Output);

        CommandResult excess = bank.Execute("deposit savings 0.01");
        Assert.Equal("ERROR BALANCE_LIMIT", excess.Output);
        BankingFacts facts = Assert.IsType<BankingFacts>(excess.Facts);
        Assert.Equal("BALANCE_LIMIT", facts.Error);
        Assert.False(facts.Succeeded);
        Assert.Empty(facts.Balances);
        Assert.Equal($"chequing=${Maximum} | savings=${Maximum}", bank.Execute("balance").Output);
    }

    [Fact]
    public void TransferChecksAvailableFundsBeforeTheDestinationLimit()
    {
        var bank = new BankSession();
        bank.Execute($"deposit savings {Maximum}");

        CommandResult result = bank.Execute("transfer chequing savings 0.01");

        Assert.Equal("ERROR INSUFFICIENT_FUNDS | chequing=$0.00", result.Output);
        BankingFacts facts = Assert.IsType<BankingFacts>(result.Facts);
        Assert.Equal("INSUFFICIENT_FUNDS", facts.Error);
        Assert.Equal(new AccountBalance("chequing", 0m), Assert.Single(facts.Balances));
        Assert.Equal($"chequing=$0.00 | savings=${Maximum}", bank.Execute("balance").Output);
    }

    [Fact]
    public void SameAccountTransferTakesPrecedenceOverInsufficientFunds()
    {
        var bank = new BankSession();

        CommandResult result = bank.Execute("transfer Savings SAVINGS 0.01");

        Assert.Equal("ERROR SAME_ACCOUNT", result.Output);
        BankingFacts facts = Assert.IsType<BankingFacts>(result.Facts);
        Assert.Equal("SAME_ACCOUNT", facts.Error);
        Assert.Equal("savings", facts.From);
        Assert.Equal("savings", facts.To);
        Assert.Empty(facts.Balances);
        Assert.Equal("chequing=$0.00 | savings=$0.00", bank.Execute("balance").Output);
    }

    [Fact]
    public void CommandAmountsAndOutputAreInvariantUnderCommaDecimalCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-CA");
            var bank = new BankSession();

            Assert.Equal("DEPOSIT $0.50 TO savings | savings=$0.50", bank.Execute("deposit savings .50").Output);
            Assert.Equal("ERROR INVALID_AMOUNT", bank.Execute("deposit savings 1,50").Output);
            Assert.Equal("WITHDRAW $0.50 FROM savings | savings=$0.00", bank.Execute("withdraw savings 0.50").Output);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void SeparateSessionsMaintainIndependentLedgers()
    {
        var first = new BankSession();
        var second = new BankSession();

        first.Execute("deposit savings 4.57");
        second.Execute("deposit chequing 1.23");

        Assert.Equal("chequing=$0.00 | savings=$4.57", first.Execute("balance").Output);
        Assert.Equal("chequing=$1.23 | savings=$0.00", second.Execute("balance").Output);
    }

    [Fact]
    public void MixedTransactionsMatchAnIntegerCentLedgerAfterEachCommand()
    {
        var bank = new BankSession();
        var random = new Random(7421);
        string[] accounts = ["chequing", "savings"];
        int[] cents = [0, 0];

        for (int step = 0; step < 250; step++)
        {
            int source = random.Next(accounts.Length);
            int destination = 1 - source;
            int amount = random.Next(1, 400);
            string number = Dollars(amount);
            string command;
            bool shouldSucceed;
            switch (random.Next(3))
            {
                case 0:
                    command = $"deposit {accounts[source]} {number}";
                    cents[source] += amount;
                    shouldSucceed = true;
                    break;
                case 1:
                    command = $"withdraw {accounts[source]} {number}";
                    shouldSucceed = cents[source] >= amount;
                    if (shouldSucceed)
                    {
                        cents[source] -= amount;
                    }

                    break;
                default:
                    command = $"transfer {accounts[source]} {accounts[destination]} {number}";
                    shouldSucceed = cents[source] >= amount;
                    if (shouldSucceed)
                    {
                        cents[source] -= amount;
                        cents[destination] += amount;
                    }

                    break;
            }

            BankingFacts facts = Assert.IsType<BankingFacts>(bank.Execute(command).Facts);
            Assert.True(facts.Succeeded == shouldSucceed, $"Step {step}, command {command}");
            string expected = $"chequing=${Dollars(cents[0])} | savings=${Dollars(cents[1])}";
            string actual = bank.Execute("balance").Output;
            Assert.True(expected == actual, $"Step {step}, command {command}. Expected {expected}. Actual {actual}.");
        }
    }

    private static string Dollars(int cents) => FormattableString.Invariant($"{cents / 100}.{cents % 100:D2}");
}
