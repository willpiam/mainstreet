namespace BankingCli.Ai;

internal interface IChatModel
{
    Task<string?> CompleteAsync(string instructions, string input, string schemaName, string schema);
}
