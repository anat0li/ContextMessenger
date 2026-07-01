# ContextMessenger

ContextMessenger is a Windows desktop companion app that bridges a chat client to local
developer resources through a turn-based text protocol. A chat client emits a
`BEGIN_REQUEST … END_REQUEST` JSON block; ContextMessenger scans it out of the chat window, runs
the commands against a sandboxed root, and posts a correlated `BEGIN_RESPONSE … END_RESPONSE`
block back.

The value proposition is **reach, not depth**: it gives chat clients that have no native local
tool access (for example ChatGPT or Claude desktop apps) a way to read,
navigate, and *edit* a real .NET repository — and to run read-only queries against a local SQL
database — on the user's machine.

> **Status: alpha (V1).** The protocol, command surface, and settings format may still change.
> Read the [Security model](#security-model) before pointing it at anything you care about.

## How it works

1. You configure one or more **roots** (a local code folder, or a SQL database) and one or more
   **targets** (a chat client). A target + root pair is a *processing loop*.
2. The chat client is given a system prompt describing the protocol and available commands
   (see [`docs/chat-prompt.md`](docs/chat-prompt.md)).
3. When the model emits a `BEGIN_REQUEST … END_REQUEST` block, the app reads it from the chat
   window via UI Automation, dispatches the commands, and writes a `BEGIN_RESPONSE … END_RESPONSE`
   block back through the clipboard.
4. Mutating operations (code patches) run through a transactional workflow with optional
   build/test gating and human-in-the-loop diff review.

## Command surface

Commands are grouped into families. Which families are available depends on the active root's
kind; `capabilities` reports what the current root actually supports.

| Family | Mutating? | Commands |
| --- | --- | --- |
| **Session** | no | `current_context`, `list_roots`, `set_root`, `list_targets`, `capabilities` |
| **Filesystem** | no | `tree`, `read_file`, `search_text`, `list_files`, `project_info` |
| **Roslyn** | no | `document_symbols`, `find_symbol`, `find_references`, `goto_definition`, `find_implementations`, `find_callers`, `find_derived_types`, `find_overrides`, `get_symbol_info`, `get_symbol_source` |
| **Patching** | **yes** | `git_status`, `propose_patch`, `amend_patch`, `validate_patch`, `current_patch`, `revert_patch` |
| **SQL** (read-only) | no | `sql_schema`, `sql_tables`, `sql_columns`, `sql_query` |

A code root exposes the filesystem, Roslyn, and patching families. A SQL root exposes only the
SQL family. Session commands are always available.

## Quick start

1. Build and run the WPF app.
2. Add a **code root** pointing at a local Git repository (the patching and Roslyn families expect
   a Git working tree).
3. Add a **target** for the chat-client window you want ContextMessenger to watch.
4. Copy the protocol prompt (see [`docs/chat-prompt.md`](docs/chat-prompt.md)) into that chat as its
   project/system instructions.
5. Ask the model to send a `current_context`, `project_info`, or `tree` request, and watch the app
   post the response back.
6. Review any proposed patch in the diff view before accepting it.

Adjust the exact steps to match the current UI.

## Requirements

- Windows 10/11
- .NET 10 SDK
- A supported chat client (e.g. ChatGPT or Claude desktop app)

## Build and run

```bash
# Build everything
dotnet build ContextMessenger.slnx

# Run the app
dotnet run --project src/ContextMessenger.App.Wpf/ContextMessenger.App.Wpf.csproj
```

Close the running app before rebuilding it — Windows locks the copied DLLs under
`bin/Debug/net10.0-windows` while the app is running.

### Tests

Each source library has a matching test project under `src/tests`:

```bash
dotnet test src/tests/ContextMessenger.FileSystem.Tests/ContextMessenger.FileSystem.Tests.csproj
dotnet test src/tests/ContextMessenger.Protocol.Tests/ContextMessenger.Protocol.Tests.csproj
dotnet test src/tests/ContextMessenger.Roslyn.Tests/ContextMessenger.Roslyn.Tests.csproj
dotnet test src/tests/ContextMessenger.Patching.Tests/ContextMessenger.Patching.Tests.csproj
dotnet test src/tests/ContextMessenger.Data.Tests/ContextMessenger.Data.Tests.csproj
dotnet test src/tests/ContextMessenger.App.Wpf.Tests/ContextMessenger.App.Wpf.Tests.csproj
```

Some SQL integration tests require a local SQL Server Express instance and skip themselves when
it is not present; the default suite runs against SQLite.

## Project layout

```text
ContextMessenger.Core          interfaces and DTOs (filesystem, roslyn, patching, sql, meta)
ContextMessenger.FileSystem    tree, read_file, search_text, list_files, sandbox, git/project info
ContextMessenger.Roslyn        workspace-backed navigation + syntax outline
ContextMessenger.Patching      patch transaction service, edit compiler, diff/build/test runners
ContextMessenger.Data          ADO.NET provider resolution, read-only SQL guard, query/schema services
ContextMessenger.Protocol      request parser, validator, dispatcher, writer, command catalog
ContextMessenger.App.Wpf       WPF automation host, loop runtime, patch review UI, composition root
```

Settings and logs are stored under `%AppData%\ContextMessenger`.

## Known limitations

- Windows-only.
- UI Automation and clipboard-based transport are inherently fragile and may break when chat-client
  UI changes.
- The app is intended for local, interactive use, not unattended production automation.
- SQL write protection is best-effort; use a read-only database login.
- Protocol and settings formats may change during the V1 alpha.

## Experimental: observing model behavior

Because ContextMessenger logs every request/response pair it relays (see
[Project layout](#project-layout)), it can also be used experimentally as a lightweight
instrumentation harness for studying how a model performs a task: the log is a timestamped
transcript of the exact command sequence the model chose, including retries, backtracking, and how
it allocates a limited number of turns or tool calls across a task. This is a secondary,
exploratory use case rather than the tool's primary design goal, so treat conclusions drawn from a
small number of runs cautiously. As with any other use, be mindful that these logs can capture the
same data described in [Security model](#security-model) below.

## Security model

ContextMessenger deliberately gives a chat model the ability to act on your machine. That is the
point of the tool, and it is also its risk surface. Understand these trust boundaries before use,
and prefer running it against repositories and databases you are comfortable exposing to the
model.

- **Local data may be sent to the chat provider.** When ContextMessenger returns file contents,
  source snippets, directory listings, build output, or SQL query results, that text is pasted into
  the chat client. Depending on the client and account settings, it may be processed by the remote
  model provider. Do not expose repositories, logs, secrets, customer data, or databases unless you
  are comfortable sending the returned text to that provider.
- **Prompt injection is in scope.** The app acts on text produced by the model, which can be
  influenced by content the model read (files, query results, web context in the chat). Treat the
  model's requests as untrusted input that you have chosen to execute locally.
- **The patching family mutates your working tree.** `propose_patch` / `amend_patch` write files
  and can run builds and tests. Mitigations built in: patches require a clean git tree, are applied
  transactionally with atomic writes, can be held for human diff review before acceptance, and
  `revert_patch` refuses to run if `HEAD` has moved off the patch base. Use a dedicated branch and
  review diffs; nothing here replaces your judgment.
- **SQL roots are read-only by intent, not by guarantee.** The built-in guard rejects non-`SELECT`
  statements and multi-statement batches, and connections are opened read-only where the provider
  supports it — but a text-based guard is **defense-in-depth**. The strong control is operational:
  point SQL roots at a **least-privilege, read-only database login**. Do not connect a SQL root with
  an account that can write.
- **The app reads on-screen chat text and uses the clipboard.** It scrapes the chat window via UI
  Automation and posts responses through the clipboard, so it can see what is rendered in that
  window and will overwrite clipboard contents while running.
- **Connection strings are secrets.** They are stored with the app's settings under `%AppData%`.
  Do not commit them, and prefer integrated/Windows authentication or a read-only login over
  embedding credentials.

If you find a security issue, please contact the maintainer privately first. If GitHub private
vulnerability reporting is enabled for this repository, use that; otherwise use the contact method
listed in the maintainer profile. Please do not disclose vulnerabilities publicly until they can be
addressed.

## Contributing

Issues and small pull requests are welcome while the project is in alpha. By submitting a pull
request, you agree that your contribution is licensed under the MIT License used by this repository.

## License and name

Code is licensed under the [MIT License](LICENSE).

The **ContextMessenger** name and logo are not licensed for use in derivative projects without
permission. Forks should use a different name unless authorized.

ContextMessenger is an independent project and is not affiliated with, endorsed by, or sponsored
by OpenAI, Anthropic, or any chat-client provider. Product names such as ChatGPT and Claude are
used only to describe compatible clients.
