using BankingCli.Ai;

namespace BankingCli;

internal sealed class ConversationLoop(
    BankSession bank, IConsoleIO console, IChatModel? model, bool approvalRequired)
{
    private readonly RequestInterpreter? interpreter = model is null ? null : new RequestInterpreter(model);
    private readonly ReplyWriter? replyWriter = model is null ? null : new ReplyWriter(model);

    public async Task RunAsync()
    {
        while (true)
        {
            if (!console.IsInputRedirected)
            {
                console.Write("bank> ");
            }

            string? input = console.ReadLine();
            if (input is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            if (IsCommand(input))
            {
                CommandResult result = bank.Execute(input);
                console.WriteLine(result.Output);
                if (result.ShouldExit)
                {
                    return;
                }

                continue;
            }

            if (interpreter is null)
            {
                console.WriteLine("AI is not configured. Type help for commands.");
                continue;
            }

            Interpretation interpretation = await interpreter.InterpretAsync(input);
            if (interpretation.Command is not { } command)
            {
                console.WriteLine(interpretation.Message!);
                continue;
            }

            if (approvalRequired && interpretation.Preview is { } preview && !Confirm(preview))
            {
                continue;
            }

            CommandResult executed = bank.Execute(command);
            console.WriteLine(await replyWriter!.WriteAsync(executed));
        }
    }

    private bool Confirm(string preview)
    {
        if (console.IsInputRedirected)
        {
            console.WriteLine("This request requires interactive confirmation. No changes made.");
            return false;
        }

        console.Write($"{preview}? [y/N] ");
        string? answer = console.ReadLine()?.Trim();
        if (string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        console.WriteLine("No changes made.");
        return false;
    }

    private static bool IsCommand(string input)
    {
        string[] tokens = input.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return tokens switch
        {
            ["help" or "?" or "exit", ..] => true,
            ["balance"] => true,
            ["balance", not ("both" or "all" or "please")] => true,
            // A preposition in an account slot signals an English request, even if it is incomplete.
            ["deposit" or "withdraw", "from" or "to" or "in" or "into", ..] => false,
            ["transfer", "from" or "to", ..] or ["transfer", _, "from" or "to" or "into", ..] => false,
            ["deposit" or "withdraw", "chequing" or "savings"] => true,
            ["deposit" or "withdraw", var account, var amount] =>
                IsAmountToken(amount) || (IsAccount(account) && !amount.StartsWith('$')),
            ["transfer", "chequing" or "savings", "chequing" or "savings"] => true,
            ["transfer", var from, var to, var amount] =>
                (IsAccount(from) && IsAccount(to) && !amount.StartsWith('$'))
                || ((IsAccount(from) || IsAccount(to)) && IsAmountToken(amount)),
            _ => false
        };
    }

    private static bool IsAccount(string token) => token is "chequing" or "savings";

    // Keep validation of numeric tokens with the simulator, including invalid numeric formats.
    private static bool IsAmountToken(string token) => token.AsSpan().IndexOfAnyExcept("0123456789.+-,") < 0;
}

internal interface IConsoleIO
{
    bool IsInputRedirected { get; }
    string? ReadLine();
    void Write(string text);
    void WriteLine(string text);
}

internal sealed class SystemConsoleIO : IConsoleIO
{
    public bool IsInputRedirected => Console.IsInputRedirected;
    public string? ReadLine() => Console.ReadLine();
    public void Write(string text) => Console.Write(text);
    public void WriteLine(string text) => Console.WriteLine(text);
}
