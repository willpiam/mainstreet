using System.Text.Json;
using BankingCli.Ai;

namespace BankingCli;

internal sealed class ReplyWriter(IChatModel model)
{
    private const string Instructions = """
        Pick the most natural conversational wording for these simulator facts.
        Choose concise or explanatory for a successful operation. Choose direct_decline or
        explanatory_decline for a declined operation. Return only the wording choice.
        The app will insert the authoritative amounts, accounts, balances, and decline reason.
        """;

    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "wording": { "type": "string", "enum": ["concise", "explanatory", "direct_decline", "explanatory_decline"] }
          },
          "required": ["wording"],
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
        string? response = await model.CompleteAsync(Instructions, input, "banking_reply", Schema);
        if (string.IsNullOrWhiteSpace(response))
        {
            return result.Output;
        }

        try
        {
            using JsonDocument json = JsonDocument.Parse(response,
                new JsonDocumentOptions { AllowDuplicateProperties = false });
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || json.RootElement.GetPropertyCount() != 1
                || !json.RootElement.TryGetProperty("wording", out JsonElement wording)
                || wording.ValueKind != JsonValueKind.String)
            {
                return result.Output;
            }

            return (facts.Succeeded, wording.GetString()) switch
            {
                (true, "concise") => WriteSuccess(facts, explanatory: false),
                (true, "explanatory") => WriteSuccess(facts, explanatory: true),
                (false, "direct_decline") => WriteDecline(facts, explanatory: false),
                (false, "explanatory_decline") => WriteDecline(facts, explanatory: true),
                _ => result.Output
            };
        }
        catch (JsonException)
        {
            return result.Output;
        }
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
