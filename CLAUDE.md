# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with this repository.

## What ContextMessenger Is

ContextMessenger is a Windows desktop companion app that bridges a chat client to a local .NET codebase through a turn-based text protocol. A chat client emits a `BEGIN_REQUEST ... END_REQUEST` JSON block; ContextMessenger scans it out of the chat window, runs the commands against a sandboxed root, and posts a correlated `BEGIN_RESPONSE ... END_RESPONSE` block back. The value proposition is reach, not depth: it gives chat clients that have no native local tool access (e.g. ChatGPT/Claude in a browser) a way to read, navigate, and *edit* a real repository on the user's machine.

The command surface is read **and** write. It spans five families:

- **Filesystem** (read-only): `tree`, `read_file`, `search_text`, `list_files`, `project_info`.
- **Roslyn** (read-only, workspace-backed): `document_symbols`, `find_symbol`, `find_references`, `goto_definition`, `find_implementations`, `find_callers`, `find_derived_types`, `find_overrides`, `get_symbol_info`, `get_symbol_source`.
- **Patching** (mutating): `git_status`, `propose_patch`, `amend_patch`, `validate_patch`, `current_patch`, `revert_patch` — a transactional edit workflow with optional build/test gating and human-in-the-loop diff review. See [Patch Workflow](#patch-workflow).
- **SQL** (read-only): `sql_schema`, `sql_tables`, `sql_columns`, `sql_query` — read-only ADO.NET access to a database root. See [SQL Roots](#sql-roots).
- **Session**: `current_context`, `list_roots`, `set_root`, `list_targets`, `capabilities`.

A root has a `Kind` (`fileSystem` or `sql`). A filesystem root exposes the filesystem, Roslyn, and patching families; a SQL root exposes the SQL family only. Session commands are always available, and which families a root actually supports is reported by `capabilities`.

The WPF app is a loop-oriented automation host: a target chat client plus a root profile defines a processing loop. The UI shows loop state, logs, and the patch review surface.

> Naming: the product and project name is **ContextMessenger** (code namespaces are `ContextMessenger.*`). The empty `src/tests/ContextMessenger.Engine.Tests` folder and stray assembly artifacts under `obj/` left over from earlier renames are stale remnants of reverted commits — not part of the build.

## Project Layout

```text
ContextMessenger.Core          interfaces and DTOs (filesystem, roslyn, patching, sql, meta)
ContextMessenger.FileSystem    tree, read_file, search_text, list_files, sandbox, git/project info
ContextMessenger.Protocol      request parser, validator, dispatcher, writer, command catalog
ContextMessenger.Roslyn        workspace-backed navigation + syntax outline (DocumentSymbolService)
ContextMessenger.Patching      patch transaction service, edit compiler, diff/build/test runners
ContextMessenger.Data          ADO.NET provider resolution, read-only SQL guard, query/schema services
ContextMessenger.App.Wpf       WPF automation host, loop runtime, patch review UI, composition root
```

Tests live under `src/tests`, one test project per source library (`FileSystem`, `Protocol`, `Roslyn`, `Patching`, `Data`, `App.Wpf`).

`ContextMessenger.App.Wpf` targets `net10.0-windows`; the library projects target `net10.0`.

## Build And Test

```bash
dotnet build ContextMessenger.slnx
dotnet build src/ContextMessenger.App.Wpf/ContextMessenger.App.Wpf.csproj
dotnet test src/tests/ContextMessenger.FileSystem.Tests/ContextMessenger.FileSystem.Tests.csproj
dotnet test src/tests/ContextMessenger.Protocol.Tests/ContextMessenger.Protocol.Tests.csproj
dotnet test src/tests/ContextMessenger.Roslyn.Tests/ContextMessenger.Roslyn.Tests.csproj
dotnet test src/tests/ContextMessenger.Patching.Tests/ContextMessenger.Patching.Tests.csproj
dotnet test src/tests/ContextMessenger.Data.Tests/ContextMessenger.Data.Tests.csproj
```

Close the running WPF app before rebuilding it. Windows will lock copied DLLs under `bin/Debug/net10.0-windows` while the app is running.

## SDK Quirk

`ContextMessenger.App.Wpf.csproj` sets `<ProduceReferenceAssembly>false</ProduceReferenceAssembly>`. Without it, the .NET 10 SDK WPF markup-compile pass and the main project can both write `obj/Debug/net10.0-windows/refint/ContextMessenger.App.Wpf.dll` and lock each other out. Do not remove this unless the SDK issue is known to be fixed.

## File-System Rules

- `PathSandbox` is the path gatekeeper. Every public file-system operation must resolve user paths through it before touching disk.
- Output paths are forward-slash relative paths.
- Default excluded directories are pruned during enumeration.
- User include/exclude globs are relative to the requested command `path`.
- `list_files` output is ordered by relative path.

## Protocol Rules

- Requests are strict JSON inside `BEGIN_REQUEST ... END_REQUEST`.
- Each request has `version`, `id`, and `commands`.
- A batch may be a single request object or an array of request objects.
- Per-command failures are returned as command result errors.
- Parse and validation failures are returned as top-level error responses.
- Request IDs are cached to prevent repeated processing of the same visible chat block.
- `docs/chat-prompt.md` must stay in sync with `SystemPromptProvider.Generate()`; the protocol tests enforce this. `CommandCatalog` is the single source of truth for the per-command descriptors that drive both the `capabilities` command and the generated prompt — add a new command there when you register a handler.
- `CommandDispatcher.ForServices(fs, roslyn, session, gitStatus, patchTransactions, dataRootSession, allowSchemaCommands, ...)` is the composition seam. Every service is optional (including `fs`); a handler family is registered only when its service is supplied. `ForFileSystem` and the shorter overloads exist for tests and minimal hosts. `SessionFactory.Create` picks the set per root: a `fileSystem` root wires fs/roslyn/gitStatus/patchTransactions; a `sql` root wires only `dataRootSession` (see [SQL Roots](#sql-roots)) and its `LoopSession.Patches` is null.
  - Session handlers (`IContextSession`) describe and switch the active root/target.
  - Filesystem and Roslyn handlers operate against the file system / Roslyn workspace bound to the active root.
  - Patching handlers (`IPatchTransactionService`, `IGitStatusService`) mutate the working tree; they are only present when App.Wpf wires a patch transaction service for the active root.
  - SQL handlers (`IDataRootSession`) run read-only queries and metadata against the active SQL root; `allowSchemaCommands` gates the `sql_schema`/`sql_tables`/`sql_columns` metadata commands.
- `set_root` maps a requested root name onto a `(target, root)` loop with `IsAutoProcessEnabled = true` via `AppRootSwitchCoordinator`. The receiving loop's auto-process flag will flip off as a side effect; the OK response is written before the loop swap takes effect.
- When a batch contains multiple `set_root` commands, only the last takes effect; the earlier ones return an `ignored` result.

## Patch Workflow

The Patching family is the only mutating surface and the highest-risk part of the app. Treat it accordingly.

- `IPatchTransactionService` owns one in-memory patch transaction at a time. `propose_patch` requires a clean working tree (`git_status` first), applies a structured full-file patch, then optionally runs a solution build and tests.
- A request body carries either `files` (full-file create/replace/delete keyed by content hash) or `edits` (lightweight textual edits — `replace_exact`, `insert_before/after_exact`, `delete_exact`, `replace_lines`, `json_set`, `replace_symbol_source`). Edits are compiled by `PatchEditCompiler` into the same full-file transaction state, so there is one validation/apply path. `replace_symbol_source` is the bridge into the Roslyn layer.
- Hash fields (`expectedFileHash`, `expectedAnchorHash`, `oldContentHash`, `oldRangeHash`, `oldSourceHash`) are optimistic-concurrency guards. Validation failures surface as structured `PatchValidationException` errors with path/edit-index/hash detail, not bare messages.
- Status vocabulary (`PatchTransactionStatuses`): `needs_revision` (build/tests failed — fix via `amend_patch`), `awaiting_acceptance` (passed but held for review), `reverted`. `accepted` is terminal: the transaction is disposed and never held.
- `DeferAcceptanceByDefault` (per-root, runtime-settable) decides whether a passing patch auto-accepts or is held in `awaiting_acceptance` for human review. When held, App.Wpf surfaces it through the patch review UI (`HeldPatchCoordinator`, `PatchReviewViewModel`); reviewer comments ride back in the next `amend_patch` and are matched to outcomes by `(requestId, commandIndex)`.
- `validate_patch` is the dry run: same compile/validation path with no apply, no build/test, no staging, no metadata. Prefer documenting/testing new edit kinds through it.

### Safety invariants

These are load-bearing; preserve them when changing `PatchTransactionService` or `FilePatchApplier`.

- **Serialized state.** All public `PatchTransactionService` operations run under a single lock (`_gate`) via `*Core` wrappers, because the loop thread (propose/amend) and the review UI thread (accept/revert) share the same instance. `DeferAcceptanceByDefault` is deliberately kept off the lock (a `volatile` flag) so toggling hold-for-review never blocks behind an in-flight build/test.
- **Single active patch, app-wide.** The session store is one slot shared across roots. Each work-starting/acting op calls `SyncForeignActiveFromStore` to re-read it, so a patch begun on another root after this service was constructed is seen and deferred to with `patch_in_progress` — rather than overwriting that root's metadata. This enforces the single-active-patch design; true per-root concurrent patches would need a per-root keyed store.
- **Crash recovery is faithful.** `PatchSessionMetadata.Files` persists each touched file's operation and content-hash anchors, so `RecoverFromStore` rebuilds the patch with its optimistic-concurrency guards intact. Legacy metadata with no `Files` falls back to inferring the set from the dirty tree (hashes unavailable).
- **Revert refuses to rewrite history.** `Revert` hard-resets the working tree to the patch base, so it first asserts `HEAD == BaseHeadSha` (`EnsureHeadMatchesBase`); if HEAD moved (e.g. the user committed outside the app) it fails with `invalid_git_state` instead of discarding intervening commits.
- **Writes are atomic and format-preserving.** `FilePatchApplier` writes each file to a sibling temp file then atomically renames it, so a crash leaves the target whole. On replace it detects and re-emits the existing file's BOM/encoding (UTF-8/UTF-16 by byte-order mark; no BOM ⇒ UTF-8) and dominant line ending, so a replacement does not produce a whole-file diff from re-encoding. Create writes UTF-8 without a BOM.
- **Control directory.** Build/test output funnels (via `--artifacts-path`) into the `.contextmessenger` control directory under the root, named once in `PatchWorkspace.ControlDirectoryName`. `LibGit2SharpGitStatusService` excludes it from status so it never dirties the tree or trips the clean-tree / diff checks.

## SQL Roots

A `sql` root (`RootProfile.Kind == RootKind.Sql`, configured by `SqlRootSettings`) gives the model read-only ADO.NET access to a database. `SessionFactory.CreateSqlSession` composes it as **session + sql only** (no filesystem/Roslyn/patching/git), and the loop's `LoopSession.Patches` is null.

- **Pipeline (`ContextMessenger.Data`).** `ReflectionDataProviderResolver` resolves the ADO.NET `DbProviderFactory` (by invariant name, assembly path, or factory type); `DataConnectionFactory` opens the connection; `DataSchemaReader` answers metadata commands and `DataQueryService` runs queries through `ReadOnlySqlGuard`. `DataRootSession` ties them together and backs the SQL handlers.
- **Read-only is enforced in layers, none sufficient alone.** `SessionFactory` refuses a SQL root unless `SqlRootSettings.ReadOnly` is set; the connection is opened read-only where the provider supports it; `ReadOnlySqlGuard` rejects non-`SELECT` and multi-statement SQL. The guard is **defense-in-depth** — the load-bearing control is operational: point the root at a least-privilege, read-only database login.
- **Limits.** `MaxRows`, `MaxCellBytes`, and `CommandTimeoutSeconds` bound each result (also keeping it within the clipboard transport budget); `allowSchemaCommands` gates the metadata commands. Errors surface as `sql_*` codes (`sql_provider_not_found`, `sql_connection_failed`, `sql_not_read_only`, `sql_timeout`, `sql_schema_unavailable`, `sql_table_not_found`, `sql_query_failed`).
- **Secrets.** The connection string is resolved through `ISqlConnectionStringResolver` (App.Wpf's `SqlConnectionStringResolver`, e.g. the `literal:` scheme) and must never be logged.

## Loop Runtime Model

- A processing loop = one target profile + one root profile. `LoopManager` owns the lifecycle: it lazily creates an `IMessageProcessingLoop` runtime (plus a `LoopPatchContext`) per loop via `ILoopRuntimeFactory`, then re-raises each runtime's background events (`LogProduced`, `StatusChanged`, `PatchInteractionChanged`) for the host view-model.
- Keep VM-coupled reactions (status display, UI logging, review routing) in the view-model; keep registry/lifecycle bookkeeping in `LoopManager` so it stays unit-testable.

## WPF App Notes

- Settings and logs are stored under `%AppData%\ContextMessenger`.
- Target-specific UI automation settings live in target profiles.
- The scanner reads chat-window UI Automation text and may see rendered markdown, not raw model text. The glob parser intentionally tolerates known markdown-stripped forms such as `.cs` and `/.cs`.
- `TargetAutomationSettings.RepairUnterminatedQuotes` (default off) is an opt-in recovery for chat clients that emit unescaped quotes inside free-text values. When a request body fails to parse, the scanner retries it through `ContextMessenger.Protocol.Json.Lexer.Escape`, which folds unescaped quotes into the listed `DefaultTerminatedKeys` fields (each value must be on its own line so the newline-termination heuristic applies). Repair runs only as a fallback: valid requests are never rewritten, and a failed repair degrades to the normal invalid-candidate handling. For V1, ClipboardUi remains the supported transport; structured transports are deferred.
- Avoid putting protocol orchestration into view code. The ViewModel coordinates loops; protocol and file-system behavior should remain in library projects.

## Deferred Roslyn Tasks

The readonly Roslyn layer currently supports syntax-only `document_symbols` plus workspace-backed `find_symbol`, `find_references`, `goto_definition`, `find_implementations`, `find_callers`, `find_derived_types`, `find_overrides`, `get_symbol_info`, and `get_symbol_source` (the last also backs the `replace_symbol_source` edit kind). Keep the next semantic additions deliberately scoped.

Deferred items:

- Add `span` objects to symbol results when editor-style positioning is needed.
- Add `assembly` only if project name is not enough for multi-solution or package scenarios.
- Add optional reference context lines only when single-line reference text proves insufficient.
- Add `type_hierarchy` only if derived-type and implementation queries are not enough in practice.
- Revisit `project_symbols` later; its scope should be informed by real `find_symbol` usage.
- Do not serialize source-generator output into `document_symbols`; that command remains a fast syntax outline. Generated members belong in semantic/workspace-backed commands.
