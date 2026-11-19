using System.Globalization;

namespace BankingCli;

internal sealed class BankSession
{
    private const string Chequing = "chequing";
    private const string Savings = "savings";

    // Leave room for the sum of two valid amounts without losing cent precision.
    private const decimal MaximumBalance = 99_999_999_999_999_999_999_999_999.99m;

    private const string Help = """
        ACCOUNTS chequing | savings
        deposit <account> <amount>
        withdraw <account> <amount>
        transfer <from> <to> <amount>
        balance [account]
        help | ?
        exit
        """;

    private readonly Dictionary<string, decimal> balances = new Dictionary<string, decimal>()
    {
        [Chequing] = 0m,
        [Savings] = 0m
    };

    public CommandResult Execute(string input)
    {
        string[] tokens = input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return new CommandResult(string.Empty);
        }

        string command = tokens[0].ToLowerInvariant();
        if (command == "exit" && tokens.Length == 1)
        {
            return new CommandResult("EXIT", ShouldExit: true);
        }

        return command switch
        {
            "balance" => ShowBalance(tokens),
            "deposit" or "withdraw" or "transfer" => Transact(command, tokens),
            "help" or "?" when tokens.Length == 1 => new CommandResult(Help),
            "help" or "?" or "exit" => new CommandResult("ERROR INVALID_ARGUMENTS"),
            _ => new CommandResult("ERROR UNKNOWN_COMMAND")
        };
    }

    private CommandResult ShowBalance(string[] tokens)
    {
        if (tokens.Length == 1)
        {
            return BalanceResult([GetBalance(Chequing), GetBalance(Savings)]);
        }

        if (tokens.Length != 2)
        {
            return new CommandResult("ERROR INVALID_ARGUMENTS");
        }

        string account = tokens[1].ToLowerInvariant();
        return balances.ContainsKey(account)
            ? BalanceResult([GetBalance(account)])
            : new CommandResult("ERROR INVALID_ACCOUNT");
    }

    private static CommandResult BalanceResult(AccountBalance[] balances) => new(
        string.Join(" | ", balances.Select(FormatBalance)),
        Facts: new BankingFacts("balance", null, null, null, balances));

    private CommandResult Transact(string command, string[] tokens)
    {
        bool isTransfer = command == "transfer";
        if (tokens.Length != (isTransfer ? 4 : 3))
        {
            return new CommandResult("ERROR INVALID_ARGUMENTS");
        }

        string account = tokens[1].ToLowerInvariant();
        string destination = isTransfer ? tokens[2].ToLowerInvariant() : account;
        if (!balances.ContainsKey(account) || !balances.ContainsKey(destination))
        {
            return new CommandResult("ERROR INVALID_ACCOUNT");
        }

        if (!TryParseAmount(tokens[^1], out decimal amount))
        {
            return new CommandResult("ERROR INVALID_AMOUNT");
        }

        string? from = command == "deposit" ? null : account;
        string? to = command == "withdraw" ? null : destination;
        CommandResult Result(string output, AccountBalance[] reported, string? error = null) =>
            new(output, Facts: new BankingFacts(command, from, to, amount, reported, error));

        if (isTransfer && account == destination)
        {
            return Result("ERROR SAME_ACCOUNT", [], "SAME_ACCOUNT");
        }

        if (command != "deposit" && amount > balances[account])
        {
            AccountBalance available = GetBalance(account);
            return Result($"ERROR INSUFFICIENT_FUNDS | {FormatBalance(available)}", [available], "INSUFFICIENT_FUNDS");
        }

        if (command != "withdraw" && balances[destination] + amount > MaximumBalance)
        {
            return Result("ERROR BALANCE_LIMIT", [], "BALANCE_LIMIT");
        }

        // A rejected transfer must leave both accounts intact, so validate both sides before updating either.
        if (command != "deposit")
        {
            balances[account] -= amount;
        }

        if (command != "withdraw")
        {
            balances[destination] += amount;
        }

        string formattedAmount = FormatMoney(amount);
        AccountBalance accountBalance = GetBalance(account);
        if (isTransfer)
        {
            AccountBalance destinationBalance = GetBalance(destination);
            return Result($"TRANSFER {formattedAmount} FROM {account} TO {destination} | " +
                $"{FormatBalance(accountBalance)} | {FormatBalance(destinationBalance)}", [accountBalance, destinationBalance]);
        }

        string action = command == "deposit" ? "DEPOSIT" : "WITHDRAW";
        string direction = command == "deposit" ? "TO" : "FROM";
        return Result($"{action} {formattedAmount} {direction} {account} | {FormatBalance(accountBalance)}", [accountBalance]);
    }

    internal static bool TryParseAmount(string input, out decimal amount)
    {
        amount = 0m;
        int decimalPoint = input.IndexOf('.');
        if (decimalPoint >= 0 && input.Length - decimalPoint - 1 is < 1 or > 2)
        {
            return false;
        }

        // Keep the decimal separator consistent across locales in both commands and results.
        return decimal.TryParse(input, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount)
            && amount > 0m
            && amount <= MaximumBalance;
    }

    private AccountBalance GetBalance(string account) => new(account, balances[account]);

    private static string FormatBalance(AccountBalance balance) => $"{balance.Account}={FormatMoney(balance.Amount)}";

    internal static string FormatMoney(decimal amount) => "$" + amount.ToString("F2", CultureInfo.InvariantCulture);
}

internal readonly record struct CommandResult(string Output, bool ShouldExit = false, BankingFacts? Facts = null);

internal sealed record BankingFacts(
    string Operation, string? From, string? To, decimal? Amount, AccountBalance[] Balances, string? Error = null)
{
    public bool Succeeded => Error is null;
}

internal readonly record struct AccountBalance(string Account, decimal Amount);
