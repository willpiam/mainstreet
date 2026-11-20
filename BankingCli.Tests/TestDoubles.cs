using BankingCli.Ai;

namespace BankingCli.Tests;

internal sealed class ScriptedModel(params string?[] responses) : IChatModel
{
    public Queue<string?> Responses { get; } = new(responses);
    public List<(string Schema, string Input)> Calls { get; } = [];

    public Task<string?> CompleteAsync(string instructions, string input, string schemaName, string schema)
    {
        Calls.Add((schemaName, input));
        return Task.FromResult(Responses.Dequeue());
    }
}

internal sealed class ScriptedConsole(string?[] input, bool redirected) : IConsoleIO
{
    private readonly Queue<string?> lines = new(input);
    public StringWriter Output { get; } = new() { NewLine = "\n" };
    public bool IsInputRedirected => redirected;
    public string? ReadLine() => lines.TryDequeue(out string? line) ? line : null;
    public void Write(string text) => Output.Write(text);
    public void WriteLine(string text) => Output.WriteLine(text);
}
