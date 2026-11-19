using Xunit;
using BankingCli;

namespace BankingCli.Tests;

public sealed class AppConfigTests
{
    private static readonly string[] Keys =
    [
        "OPENAI_API_KEY", "OPENAI_MODEL", "OPENAI_BASE_URL", "OPENAI_REASONING", "USER_APPROVAL_REQUIRED"
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
