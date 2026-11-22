# mainstreet banking CLI

Built on [dpo007/banking-cli-starter @ b7eb68e](https://github.com/dpo007/banking-cli-starter/tree/b7eb68e242e7723613321b3b60473c7c29591d20). Run `git diff b7eb68e..HEAD` to see every change made on top of the starter.

A C# command-line banking simulator with in-memory chequing and savings accounts. Both accounts start at $0.00 each time you run the program. Use the original commands or configure an OpenAI-compatible API to make requests in plain English.

## Run

With the .NET 10 SDK installed, run this from the repo root.

```sh
dotnet build BankingCli.sln
dotnet run --project BankingCli
```

In Visual Studio 2026 (18.0 or later), open `BankingCli.sln` and press F5. Visual Studio 2022 users can run the app from a terminal using the .NET 10 SDK.

## Commands

| Command | Effect |
| --- | --- |
| `deposit <account> <amount>` | Add money to an account. |
| `withdraw <account> <amount>` | Take money out of an account. |
| `transfer <from> <to> <amount>` | Move money between accounts. |
| `balance [account]` | Show one balance or both balances when the account is omitted. |
| `help` or `?` | Show the command list. |
| `exit` | End the session. |

Accounts are `chequing` and `savings`. Amounts are positive, with up to two decimal places, such as `5` or `5.25`. Withdrawals and transfers require sufficient funds, and transfers require two different accounts. A failed command leaves both balances as they were.

```text
bank> deposit savings 10
DEPOSIT $10.00 TO savings | savings=$10.00
bank> transfer savings chequing 4
TRANSFER $4.00 FROM savings TO chequing | savings=$6.00 | chequing=$4.00
bank> withdraw chequing 1
WITHDRAW $1.00 FROM chequing | chequing=$3.00
bank> balance
chequing=$3.00 | savings=$6.00
bank> exit
EXIT
```

## API configuration

Run from the repository root. Copy `.env.example` to `.env`, or set the variables in your shell. Process environment variables take precedence over `.env`. Direct banking commands work without API configuration.

| Variable | Meaning |
| --- | --- |
| `OPENAI_API_KEY` | Your provider's API key. Keep it in your environment or the gitignored `.env` file. |
| `OPENAI_MODEL` | Set to `gpt-6-luna` for this setup. Other models must support Chat Completions with strict JSON Schema output. |
| `OPENAI_BASE_URL` | Optional API base URL, such as `https://api.openai.com/v1`. The SDK appends `/chat/completions`. If unset, the SDK uses OpenAI's default endpoint. |
| `OPENAI_REASONING` | Set to `low` for this setup. This setting is optional when using other models. |
| `USER_APPROVAL_REQUIRED` | Optional, defaults to `false`. Set it to `true` to preview and confirm plain-English deposits, withdrawals, and transfers. |

With approval enabled, answer `y` or `yes` to execute the previewed transaction. Approval requires interactive input. For piped input the CLI skips the transaction and continues with the next line. Balance requests and direct commands run without an approval prompt.

## Plain-English examples

- “Deposit ten dollars into savings.”
- “Transfer four dollars from savings to chequing.”
- “Withdraw a dollar from chequing.”
- “How much is in savings?”
- “Show both account balances.”

Submit one operation per line. Clarification comes before approval, even when approval is enabled. If asked for clarification, include all details when you retry. Malformed positional commands show the simulator's error directly. If the reply API is unavailable after a command runs, the CLI prints the simulator's original result.

## Verification

Run `dotnet test BankingCli.sln` to verify the command flow and the SDK request and response handling using test doubles. I also ran the following checks in a single session against a local, scripted OpenAI-compatible HTTP endpoint. That exercised both API calls and the simulator. Live model behavior remains unverified.

| Check | Steps and observed result |
| --- | --- |
| Successful transfer | Ran `deposit savings 10`, entered “Transfer four dollars from savings to chequing”, then ran `balance`. The reply reported the $4.00 transfer from savings to chequing. `balance` showed $6.00 in savings and $4.00 in chequing. |
| Missing destination | Entered “Transfer four dollars out of savings”, then ran `balance`. The CLI asked for both the source and destination and to try again. Savings stayed at $6.00 and chequing at $4.00. |
| Insufficient funds | Entered “Transfer seven dollars from savings to chequing”, then ran `balance`. The reply explained that savings had insufficient funds. Savings stayed at $6.00 and chequing at $4.00. |

In a fresh session seeded with $10.00 in savings, I also returned an HTTP error from the reply endpoint after a $4.00 transfer. The CLI printed the raw transfer result and the next `balance` showed that the $4.00 had moved exactly once.

## How I worked

- I set up a copy of the starter and used Cursor with Claude Opus 5.5 to draft a plan that included a transaction preview. [Implementation plan](.cursor/plans/nl_banking_with_approval_9f2c99f4.plan.md)
- I used Codex with GPT-6.2 Sol to review the plan against the exercise and implement and test the revised flow.
- We changed the reply design after Codex flagged that an amount-only check would accept swapped balances. I verified the replacement with a test that checks the account-to-balance pairs sent to the reply API and a CLI run whose final balance matched the conversational reply.
- wrote automated tests
- ran the program myself and messed around with it for a while. Eventually I found the inputs which lead to the wrong actions.. such as.. 
    - "transfer a dollar 15 from savings to checking" which transfered $15 dollars instead of $1.15
    - *The exercise says to run a plain-English request only when it maps cleanly onto one command, and to ask for a missing or unclear detail before changing either balance.*
    - I will note that if `USER_APPROVAL_REQUIRED` had been enabled the user would have been able to catch this before approval 
    - using claude I created [A plan to clarify ambiguous amounts](.cursor/plans/clarify_ambiguous_amounts_d566e37a.plan.md)
- in a seperate branch decided to absolutly spam tests. Created 111 new tests 
    - caught an intreasting case. "What would happen if I put $5 into savings?"
    - decided to merge these tests into master