using BankingCli;
using BankingCli.Ai;

AppConfig config = AppConfig.Load();
IChatModel? model = OpenAiChatModel.Create(config);
await new ConversationLoop(
    new BankSession(), new SystemConsoleIO(), model, config.ApprovalRequired, config.Summary).RunAsync();
