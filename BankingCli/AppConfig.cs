namespace BankingCli;

internal sealed record AppConfig(
    string? ApiKey, string? Model, Uri? Endpoint, string? Reasoning, bool ApprovalRequired)
{
    public static AppConfig Load()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (File.Exists(".env"))
        {
            foreach (string line in File.ReadLines(".env"))
            {
                string entry = line.Trim();
                int separator = entry.IndexOf('=');
                if (entry.StartsWith('#') || separator < 1)
                {
                    continue;
                }

                string value = entry[(separator + 1)..].Trim();
                if (value.Length >= 2 && value[0] is ('"' or '\'') && value[^1] == value[0])
                {
                    value = value[1..^1];
                }

                values[entry[..separator].Trim()] = value;
            }
        }

        string? Get(string key) => Environment.GetEnvironmentVariable(key) ?? values.GetValueOrDefault(key);

        string? key = Get("OPENAI_API_KEY");
        string? model = Get("OPENAI_MODEL")?.Trim();
        string? baseUrl = Get("OPENAI_BASE_URL")?.Trim();
        Uri? endpoint = null;
        if (!string.IsNullOrEmpty(baseUrl)
            && (!Uri.TryCreate(baseUrl, UriKind.Absolute, out endpoint)
                || endpoint.Scheme is not ("http" or "https")
                || endpoint.UserInfo.Length > 0))
        {
            // An invalid custom URL must not send the configured key to the default endpoint.
            key = null;
        }

        bool approval = bool.TryParse(Get("USER_APPROVAL_REQUIRED"), out bool required) && required;
        return new AppConfig(key?.Trim(), model, endpoint, Get("OPENAI_REASONING")?.Trim(), approval);
    }
}
