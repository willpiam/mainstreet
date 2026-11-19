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

        string output = command switch
        {
            "balance" => ShowBalance(tokens),
            "deposit" or "withdraw" or "transfer" => Transact(command, tokens),
            "help" or "?" when tokens.Length == 1 => Help,
            "help" or "?" or "exit" => "ERROR INVALID_ARGUMENTS",
            _ => "ERROR UNKNOWN_COMMAND"
        };

        return new CommandResult(output);
    }

    private string ShowBalance(string[] tokens)
    {
        if (tokens.Length == 1)
        {
            return $"{FormatBalance(Chequing)} | {FormatBalance(Savings)}";
        }

        if (tokens.Length != 2)
        {
            return "ERROR INVALID_ARGUMENTS";
        }

        string account = tokens[1].ToLowerInvariant();
        return balances.ContainsKey(account) ? FormatBalance(account) : "ERROR INVALID_ACCOUNT";
    }

    private string Transact(string command, string[] tokens)
    {
        bool isTransfer = command == "transfer";
        if (tokens.Length != (isTransfer ? 4 : 3))
        {
            return "ERROR INVALID_ARGUMENTS";
        }

        string account = tokens[1].ToLowerInvariant();
        string destination = isTransfer ? tokens[2].ToLowerInvariant() : account;
        if (!balances.ContainsKey(account) || !balances.ContainsKey(destination))
        {
            return "ERROR INVALID_ACCOUNT";
        }

        if (!TryParseAmount(tokens[^1], out decimal amount))
        {
            return "ERROR INVALID_AMOUNT";
        }

        if (isTransfer && account == destination)
        {
            return "ERROR SAME_ACCOUNT";
        }

        if (command != "deposit" && amount > balances[account])
        {
            return $"ERROR INSUFFICIENT_FUNDS | {FormatBalance(account)}";
        }

        if (command != "withdraw" && balances[destination] + amount > MaximumBalance)
        {
            return "ERROR BALANCE_LIMIT";
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
        return command switch
        {
            "deposit" => $"DEPOSIT {formattedAmount} TO {account} | {FormatBalance(account)}",
            "withdraw" => $"WITHDRAW {formattedAmount} FROM {account} | {FormatBalance(account)}",
            _ => $"TRANSFER {formattedAmount} FROM {account} TO {destination} | " +
                 $"{FormatBalance(account)} | {FormatBalance(destination)}"
        };
    }

    private static bool TryParseAmount(string input, out decimal amount)
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

    private string FormatBalance(string account) => $"{account}={FormatMoney(balances[account])}";

    private static string FormatMoney(decimal amount) => "$" + amount.ToString("F2", CultureInfo.InvariantCulture);
}

internal readonly record struct CommandResult(string Output, bool ShouldExit = false);
