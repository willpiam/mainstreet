using System.Text.Json;
using BankingCli.Ai;

namespace BankingCli;

internal sealed class ReplyWriter(IChatModel model, SummarySetting summary = SummarySetting.Safe)
{
    private const string SafeInstructions = """
        Pick the most natural conversational wording for these simulator facts.
        Choose concise or explanatory for a successful operation. Choose direct_decline or
        explanatory_decline for a declined operation. Return only the wording choice.
        The app will insert the authoritative amounts, accounts, balances, and decline reason.
        """;

    private const string SafeSchema = """
        {
          "type": "object",
          "properties": {
            "wording": { "type": "string", "enum": ["concise", "explanatory", "direct_decline", "explanatory_decline"] }
          },
          "required": ["wording"],
          "additionalProperties": false
        }
        """;

    private const string GeneratedInstructions = """
        Explain this simulator result in everyday English. The reply you write is what the user will read.
        Use only these facts. Preserve the outcome, amounts, accounts, and balances.
        Format every amount as dollars and cents with a dollar sign, such as $4.00.
        For a successful operation, say what happened and state the resulting balances.
        For a declined operation, explain why it was declined and say that no money was moved.
        Return that explanation in the reply field.
        """;

    private const string GeneratedSchema = """
        {
          "type": "object",
          "properties": {
            "reply": { "type": "string" }
          },
          "required": ["reply"],
          "additionalProperties": false
        }
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string> WriteAsync(CommandResult result)
    {
        if (result.Facts is not { } facts)
        {
            return result.Output;
        }

        string input = JsonSerializer.Serialize(facts, JsonOptions);
        bool generated = summary == SummarySetting.Generated;
        string? response = await model.CompleteAsync(
            generated ? GeneratedInstructions : SafeInstructions,
            input,
            "banking_reply",
            generated ? GeneratedSchema : SafeSchema);
        if (string.IsNullOrWhiteSpace(response))
        {
            return result.Output;
        }

        try
        {
            using JsonDocument json = JsonDocument.Parse(response,
                new JsonDocumentOptions { AllowDuplicateProperties = false });
            if (json.RootElement.ValueKind != JsonValueKind.Object || json.RootElement.GetPropertyCount() != 1)
            {
                return result.Output;
            }

            return generated ? ReadGeneratedReply(json.RootElement, result.Output) : ReadSafeReply(json.RootElement, facts, result.Output);
        }
        catch (JsonException)
        {
            return result.Output;
        }
    }

    private static string ReadGeneratedReply(JsonElement root, string original)
    {
        if (!root.TryGetProperty("reply", out JsonElement reply) || reply.ValueKind != JsonValueKind.String)
        {
            return original;
        }

        string text = reply.GetString()?.Trim() ?? "";
        return text.Length == 0 ? original : text;
    }

    private static string ReadSafeReply(JsonElement root, BankingFacts facts, string original)
    {
        if (!root.TryGetProperty("wording", out JsonElement wording) || wording.ValueKind != JsonValueKind.String)
        {
            return original;
        }

        return (facts.Succeeded, wording.GetString()) switch
        {
            (true, "concise") => WriteSuccess(facts, explanatory: false),
            (true, "explanatory") => WriteSuccess(facts, explanatory: true),
            (false, "direct_decline") => WriteDecline(facts, explanatory: false),
            (false, "explanatory_decline") => WriteDecline(facts, explanatory: true),
            _ => original
        };
    }

    private static string WriteSuccess(BankingFacts facts, bool explanatory)
    {
        string balances = DescribeBalances(facts.Balances);
        if (facts.Operation == "balance")
        {
            return explanatory ? $"Here's what you have available. {balances}" : balances;
        }

        return explanatory
            ? $"I've completed your request to {DescribeTransaction(facts)}. {balances}"
            : $"I {DescribeTransaction(facts, completed: true)}. {balances}";
    }

    private static string WriteDecline(BankingFacts facts, bool explanatory)
    {
        string transaction = DescribeTransaction(facts);
        string reason = facts.Error switch
        {
            "INSUFFICIENT_FUNDS" => $"There aren't enough funds in {facts.From}.",
            "SAME_ACCOUNT" => "A transfer needs two different accounts.",
            "BALANCE_LIMIT" => $"The transaction would put {facts.To} over its balance limit.",
            _ => "The simulator declined the transaction."
        };

        string opening = explanatory
            ? $"Your request to {transaction} was declined."
            : $"I couldn't {transaction}.";
        string balances = facts.Balances.Length > 0 ? $" {DescribeBalances(facts.Balances)}" : "";
        return $"{opening} {reason}{balances} No money was moved.";
    }

    private static string DescribeTransaction(BankingFacts facts, bool completed = false)
    {
        string money = BankSession.FormatMoney(facts.Amount!.Value);
        return facts.Operation switch
        {
            "deposit" => $"{(completed ? "deposited" : "deposit")} {money} into {facts.To}",
            "withdraw" => $"{(completed ? "withdrew" : "withdraw")} {money} from {facts.From}",
            _ => $"{(completed ? "transferred" : "transfer")} {money} from {facts.From} to {facts.To}"
        };
    }

    private static string DescribeBalances(AccountBalance[] balances) => "You have " +
        string.Join(" and ", balances.Select(balance => $"{BankSession.FormatMoney(balance.Amount)} in {balance.Account}")) + ".";
}
