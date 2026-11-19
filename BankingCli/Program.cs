using BankingCli;

BankSession banking = new BankSession();

while (true)
{
    // Keep redirected output limited to command results so it can be compared line by line.
    if (!Console.IsInputRedirected)
    {
        Console.Write("bank> ");
    }

    string? input = Console.ReadLine();
    if (input is null)
    {
        break;
    }

    CommandResult result = banking.Execute(input);
    if (result.Output.Length > 0)
    {
        Console.WriteLine(result.Output);
    }

    if (result.ShouldExit)
    {
        break;
    }
}
