using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json;
using BankingCli.Ai;
using OpenAI;
using OpenAI.Chat;
using Xunit;

namespace BankingCli.Tests;

public sealed class OpenAiChatModelTests
{
    private const string Schema = """
        {"type":"object","properties":{"wording":{"type":"string"}},"required":["wording"],"additionalProperties":false}
        """;

    [Theory]
    [InlineData(null)]
    [InlineData("low")]
    public async Task SendsStrictSchemaAndOnlyIncludesReasoningWhenConfigured(string? reasoning)
    {
        const string response = """
            {"choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"{\"wording\":\"concise\"}"}}]}
            """;
        using var http = new HttpClient(new Handler(async (request, cancellationToken) =>
        {
            Assert.Equal(new Uri("https://fixture.example/v1/chat/completions"), request.RequestUri);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            using JsonDocument json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            JsonElement body = json.RootElement;
            Assert.Equal("fixture-model", body.GetProperty("model").GetString());
            Assert.Collection(body.GetProperty("messages").EnumerateArray(),
                message =>
                {
                    Assert.Equal("system", message.GetProperty("role").GetString());
                    Assert.Equal("instructions", message.GetProperty("content").GetString());
                },
                message =>
                {
                    Assert.Equal("user", message.GetProperty("role").GetString());
                    Assert.Equal("input", message.GetProperty("content").GetString());
                });
            JsonElement format = body.GetProperty("response_format");
            Assert.Equal("json_schema", format.GetProperty("type").GetString());
            Assert.True(format.GetProperty("json_schema").GetProperty("strict").GetBoolean());
            Assert.Equal("banking_reply", format.GetProperty("json_schema").GetProperty("name").GetString());
            Assert.Equal(reasoning is not null, body.TryGetProperty("reasoning_effort", out JsonElement effort));
            if (reasoning is not null)
            {
                Assert.Equal(reasoning, effort.GetString());
            }

            return Respond(HttpStatusCode.OK, response);
        }));

        var model = new OpenAiChatModel(CreateClient(http), reasoning);
        Assert.Equal("""{"wording":"concise"}""", await model.CompleteAsync("instructions", "input", "banking_reply", Schema));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"error":{"message":"Fixture rejected the request"}}""")]
    [InlineData(HttpStatusCode.OK, """{"choices":[]}""")]
    [InlineData(HttpStatusCode.OK, """{"choices":{}}""")]
    [InlineData(HttpStatusCode.OK, """{"choices":[{"index":0,"finish_reason":"stop"}]}""")]
    [InlineData(HttpStatusCode.OK, """{"choices":[{"index":0,"finish_reason":"length","message":{"role":"assistant","content":"truncated"}}]}""")]
    [InlineData(HttpStatusCode.OK, """{"choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","refusal":"declined"}}]}""")]
    public async Task NormalizesProviderFailureToNoCompletion(HttpStatusCode status, string body)
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Respond(status, body))));
        var model = new OpenAiChatModel(CreateClient(http), reasoning: null);

        Assert.Null(await model.CompleteAsync("instructions", "input", "banking_reply", Schema));
    }

    [Theory]
    [InlineData("banking_request", "2.50", "I couldn't interpret that request. Please try again or type help for commands.", "chequing=$0.00 | savings=$5.00")]
    [InlineData("banking_reply", "2.50", "TRANSFER $2.50 FROM savings TO chequing | savings=$2.50 | chequing=$2.50", "chequing=$2.50 | savings=$2.50")]
    [InlineData("banking_reply", "5.01", "ERROR INSUFFICIENT_FUNDS | savings=$5.00", "chequing=$0.00 | savings=$5.00")]
    public async Task ProviderRejectionAtEachStagePreservesLedgerAndContinues(
        string failingSchema, string amount, string expectedReply, string expectedBalance)
    {
        var calls = new List<string>();
        using var http = new HttpClient(new Handler(async (request, cancellationToken) =>
        {
            using JsonDocument json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            string name = json.RootElement.GetProperty("response_format").GetProperty("json_schema").GetProperty("name").GetString()!;
            calls.Add(name);
            if (name == failingSchema)
            {
                return Respond(HttpStatusCode.BadRequest, """{"error":{"message":"Fixture rejected the request"}}""");
            }

            string intent = JsonSerializer.Serialize(new
            {
                kind = "operation", operation = "transfer", from = "savings", to = "chequing", amount,
                scope = (string?)null, message = (string?)null
            });
            string response = JsonSerializer.Serialize(new
            {
                choices = new[] { new { index = 0, finish_reason = "stop", message = new { role = "assistant", content = intent } } }
            });
            return Respond(HttpStatusCode.OK, response);
        }));

        var console = new ScriptedConsole(
            ["deposit savings 5", $"Please move ${amount} from savings to chequing.", "balance", "exit"], redirected: true);
        var bank = new BankSession();
        var model = new OpenAiChatModel(CreateClient(http), reasoning: null);
        await new ConversationLoop(bank, console, model, approvalRequired: false).RunAsync();

        string[] expectedCalls = failingSchema == "banking_request" ? ["banking_request"] : ["banking_request", "banking_reply"];
        Assert.Equal(expectedCalls, calls);
        Assert.Equal($"DEPOSIT $5.00 TO savings | savings=$5.00\n{expectedReply}\n{expectedBalance}\nEXIT\n",
            console.Output.ToString());
    }

    private static ChatClient CreateClient(HttpClient http) => new("fixture-model", new ApiKeyCredential("fixture-key"),
        new OpenAIClientOptions
        {
            Endpoint = new Uri("https://fixture.example/v1"),
            Transport = new HttpClientPipelineTransport(http)
        });

    private static HttpResponseMessage Respond(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
