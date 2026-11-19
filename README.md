# mainstreet banking CLI

Built on [dpo007/banking-cli-starter @ b7eb68e](https://github.com/dpo007/banking-cli-starter/tree/b7eb68e242e7723613321b3b60473c7c29591d20). Run `git diff b7eb68e..HEAD` to see every change made on top of the starter.

A small C# command-line banking simulator with in-memory chequing and savings accounts. Both accounts start at $0.00 each time you run the program.

## Run

With the .NET 10 SDK installed, run this from the repo root.

```sh
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

## dotenv

Copy `.env.example` to `.env` and fill in your values. The dotenv file is expected to provide the following values.

```ini
OPENAI_API_KEY="<your api key here>"
OPENAI_MODEL="gpt-6-luna"
OPENAI_REASONING="medium"
# when enabled the generated commands are not run until the user has reviewed and approved it
USER_APPROVAL_REQUIRED="true"
```

## How I worked

1. Start by setting up the project, initalizing git, grabbing a fresh openai api key, and setting up agent parameters
2. Realized user preview + approval would be a smart way of preventing the AI from being able to do something crazy. Reading the instructions its not clear if this fits within the protocol
3. Work with claude to come up with [this plan](.cursor/plans/nl_banking_with_approval_9f2c99f4.plan.md)
4. Hand off to Codex

## AI Tools Used

- Cursor + Claude Opus 5.5
- Codex + GPT Sol 6.2