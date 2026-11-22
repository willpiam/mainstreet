using Xunit;
using BankingCli;

namespace BankingCli.Tests;

public sealed class AppConfigTests
{
    private static readonly string[] Keys =
    [
        "OPENAI_API_KEY", "OPENAI_MODEL", "OPENAI_BASE_URL", "OPENAI_REASONING", "USER_APPROVAL_REQUIRED",
        "SUMMARY_SETTING"
    ];

    [Fact]
    public void FileValuesWinWhenEnvironmentOverridesAreDisabled()
    {
        WithEnvironment(new Dictionary<string, string?> { ["OPENAI_MODEL"] = "env-model" }, directory =>
        {
            AppConfig config = AppConfig.LoadFrom(directory, environmentOverrides: false);
            Assert.Equal("file-key", config.ApiKey);
            Assert.Equal("file-model", config.Model);
            Assert.Equal("file-effort", config.Reasoning);
            Assert.True(config.ApprovalRequired);
            Assert.Equal(SummarySetting.Safe, config.Summary);
        });
    }

    [Fact]
    public void EnvironmentValuesOverrideTheFileWhenEnabled()
    {
        WithEnvironment(new Dictionary<string, string?> { ["OPENAI_MODEL"] = "env-model" }, directory =>
        {
            AppConfig config = AppConfig.LoadFrom(directory, environmentOverrides: true);
            Assert.Equal("file-key", config.ApiKey);
            Assert.Equal("env-model", config.Model);
            Assert.Equal("file-effort", config.Reasoning);
        });
    }

    [Theory]
    [InlineData("api.example/v1")]
    [InlineData("ftp://api.example/v1")]
    [InlineData("https://username:password@api.example/v1")]
    public void InvalidCustomEndpointsDisableTheModelInsteadOfUsingTheDefault(string endpoint)
    {
        WithEnvironment(new Dictionary<string, string?> { ["OPENAI_BASE_URL"] = endpoint }, directory =>
        {
            AppConfig config = AppConfig.LoadFrom(directory, environmentOverrides: true);

            Assert.Equal("file-model", config.Model);
            Assert.Null(config.ApiKey);
            Assert.Null(Ai.OpenAiChatModel.Create(config));
        });
    }

    [Fact]
    public void DotEnvIgnoresCommentsAndPreservesEqualsInsideQuotedValues()
    {
        WithEnvironment(new Dictionary<string, string?>(), directory =>
        {
            File.AppendAllText(Path.Combine(directory, ".env"), "\n" + """
                # OPENAI_API_KEY=wrong
                invalid entry
                 OPENAI_API_KEY = 'file=key'
                 OPENAI_BASE_URL = "https://api.example/v1"
                 USER_APPROVAL_REQUIRED = "false"
                """);

            AppConfig config = AppConfig.LoadFrom(directory, environmentOverrides: false);

            Assert.Equal("file=key", config.ApiKey);
            Assert.Equal(new Uri("https://api.example/v1"), config.Endpoint);
            Assert.False(config.ApprovalRequired);
            Assert.Equal(SummarySetting.Safe, config.Summary);
        });
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("safe", false)]
    [InlineData("Generated", true)]
    [InlineData("no", false)]
    public void SummarySettingIsGeneratedOnlyForThatValue(string? value, bool generated)
    {
        WithEnvironment(new Dictionary<string, string?> { ["SUMMARY_SETTING"] = value }, directory =>
        {
            SummarySetting expected = generated ? SummarySetting.Generated : SummarySetting.Safe;
            Assert.Equal(expected, AppConfig.LoadFrom(directory, environmentOverrides: true).Summary);
        });
    }

    [Fact]
    public void EnvironmentSummaryOverridesTheFile()
    {
        WithEnvironment(new Dictionary<string, string?> { ["SUMMARY_SETTING"] = "safe" }, directory =>
        {
            File.AppendAllText(Path.Combine(directory, ".env"), "\nSUMMARY_SETTING=\"generated\"\n");

            Assert.Equal(SummarySetting.Safe, AppConfig.LoadFrom(directory, environmentOverrides: true).Summary);
            Assert.Equal(SummarySetting.Generated, AppConfig.LoadFrom(directory, environmentOverrides: false).Summary);
        });
    }

    private static void WithEnvironment(Dictionary<string, string?> replacements, Action<string> assert)
    {
        string directory = Directory.CreateTempSubdirectory("banking-cli-env-").FullName;
        var saved = Keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
        try
        {
            File.WriteAllText(Path.Combine(directory, ".env"), """
                OPENAI_API_KEY="file-key"
                OPENAI_MODEL="file-model"
                OPENAI_REASONING="file-effort"
                USER_APPROVAL_REQUIRED="true"
                """);
            foreach (string key in Keys)
            {
                Environment.SetEnvironmentVariable(key, null);
            }

            foreach ((string key, string? value) in replacements)
            {
                Environment.SetEnvironmentVariable(key, value);
            }

            assert(directory);
        }
        finally
        {
            foreach ((string key, string? value) in saved)
            {
                Environment.SetEnvironmentVariable(key, value);
            }

            Directory.Delete(directory, recursive: true);
        }
    }
}
