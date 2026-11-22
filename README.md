# mainstreet banking CLI

## Run

With the .NET 10 SDK installed, run this from the repo root.

```sh
dotnet build BankingCli.sln
dotnet run --project BankingCli
```

## Commands

| Command | Effect |
| --- | --- |
| `deposit <account> <amount>` | Add money to an account. |
| `withdraw <account> <amount>` | Take money out of an account. |
| `transfer <from> <to> <amount>` | Move money between accounts. |
| `balance [account]` | Show one balance or both balances when the account is omitted. |
| `help` or `?` | Show the command list. |
| `exit` | End the session. |


## API configuration

Run from the repository root. Copy `.env.example` to `.env`, or set the variables in your shell.

| Variable | Meaning |
| --- | --- |
| `OPENAI_API_KEY` | Your provider's API key. Keep it in your environment or the gitignored `.env` file. |
| `OPENAI_MODEL` | Set to `gpt-6-luna` for this setup. Other models must support Chat Completions with strict JSON Schema output. |
| `OPENAI_BASE_URL` | Optional API base URL, such as `https://api.openai.com/v1`. The SDK appends `/chat/completions`. If unset, the SDK uses OpenAI's default endpoint. |
| `OPENAI_REASONING` | Set to `low` for this setup. This setting is optional when using other models. |
| `USER_APPROVAL_REQUIRED` | Optional, defaults to `false`. Set it to `true` to preview and confirm plain-English deposits, withdrawals, and transfers. |

With `USER_APPROVAL_REQUIRED` enabled, answer `y` or `yes` to execute the previewed transaction.  

## Plain-English examples

- “Deposit ten dollars into savings.”
- “Transfer four dollars from savings to chequing.”
- “Withdraw a dollar from chequing.”
- “How much is in savings?”
- “Show both account balances.”



## How I worked

### My Process

- Read the instructions
    - The assignment seems to be interested in how I handle the messy and unsafe outputs of an LLM and turn them into instructions of higher confidence
- Decided users should have to approve every action the agent takes. Basically the agent suggests actions and the user decides to approve them or not. 
    - I made this user approval process optional with a dotenv value because I wasn't confident it was admissible given the wording of the instructions. 
- Gave Claude the instructions and my thoughts on the project and created a plan
- Implemented the plan with codex
- Tested manually and wrote automated tests
- Rechecked instructions for anything I might have missed

### AI Tools Used

- Codex with GPT
- Cursor with Opus 5.5 and Grok 4.7

### My Three Checks

| Check | Steps and observed result |
| --- | --- |
| Successful transfer | Ran `deposit savings 10`, entered “Transfer four dollars from savings to chequing”, then ran `balance`. The reply reported the $4.00 transfer from savings to chequing. `balance` showed $6.00 in savings and $4.00 in chequing. |
| Insufficient funds | Entered “Transfer seven dollars from savings to chequing”, then ran `balance`. The reply explained that savings had insufficient funds. Savings stayed at $6.00 and chequing at $4.00. |
| Ambiguous amount | Entered “transfer a dollar 15 from savings to checking”. The phrase was misread as $15.00, and the CLI transferred $15.00.  |