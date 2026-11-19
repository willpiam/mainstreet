using System.ClientModel;
using System.Text.Json;
using OpenAI;
using OpenAI.Chat;

namespace BankingCli.Ai;

internal sealed class OpenAiChatModel(ChatClient client, string? reasoning) : IChatModel
{
    public static OpenAiChatModel? Create(AppConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey) || string.IsNullOrWhiteSpace(config.Model))
        {
            return null;
        }

        var options = new OpenAIClientOptions();
        if (config.Endpoint is not null)
        {
            options.Endpoint = config.Endpoint;
        }

        var client = new ChatClient(config.Model, new ApiKeyCredential(config.ApiKey), options);
        return new OpenAiChatModel(client, config.Reasoning);
    }

    public async Task<string?> CompleteAsync(string instructions, string input, string schemaName, string schema)
    {
        var options = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                schemaName, BinaryData.FromString(schema), jsonSchemaIsStrict: true)
        };

        if (!string.IsNullOrWhiteSpace(reasoning))
        {
#pragma warning disable OPENAI001 // Only the optional reasoning setting uses the SDK's experimental API.
            options.ReasoningEffortLevel = new ChatReasoningEffortLevel(reasoning);
#pragma warning restore OPENAI001
        }

        ChatMessage[] messages = [new SystemChatMessage(instructions), new UserChatMessage(input)];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            ChatCompletion completion = await client.CompleteChatAsync(messages, options, timeout.Token);
            if (completion.FinishReason != ChatFinishReason.Stop || !string.IsNullOrEmpty(completion.Refusal))
            {
                return null;
            }

            return string.Concat(completion.Content.Select(part => part.Text));
        }
        catch (Exception exception) when (exception is ClientResultException or HttpRequestException
            or OperationCanceledException or JsonException)
        {
            return null;
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException { ParamName: "index" }
            or InvalidOperationException or NullReferenceException)
        {
            // The SDK throws these when a compatible provider returns malformed choices or messages.
            return null;
        }
    }
}
