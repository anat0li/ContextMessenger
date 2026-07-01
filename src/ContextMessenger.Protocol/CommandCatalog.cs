using ContextMessenger.Core.Meta;
using ContextMessenger.Protocol.Commands;

namespace ContextMessenger.Protocol;

public static class CommandCatalog
{
    public const string CategorySession = "session";
    public const string CategoryFileSystem = "filesystem";
    public const string CategoryPatching = "patching";
    public const string CategoryRoslyn = "roslyn";
    public const string CategorySql = "sql";

    public const string SideEffectsNone = "none";
    public const string SideEffectsSession = "session";
    public const string SideEffectsFileSystem = "filesystem";
    public const string SideEffectsExternal = "external";

    private static readonly CommandFeatureInfo PatchEditsFeature = new()
    {
        Name = "edits",
        Description = "Textual edit front end that compiles into the same full-file patch transaction state.",
        Values = ["replace_exact", "insert_before_exact", "insert_after_exact", "delete_exact", "replace_lines", "json_set", "replace_symbol_source"],
        Kinds =
        [
            new()
            {
                Kind = "replace_exact",
                Required = ["path", "oldText", "newText"],
                Optional = ["oldTextEncoding", "newTextEncoding", "expectedFileHash", "expectedAnchorHash"],
                ExpectedAnchorHashTarget = "oldText",
            },
            new()
            {
                Kind = "insert_before_exact",
                Required = ["path", "anchor", "text"],
                Optional = ["anchorEncoding", "textEncoding", "expectedFileHash", "expectedAnchorHash"],
                ExpectedAnchorHashTarget = "anchor",
            },
            new()
            {
                Kind = "insert_after_exact",
                Required = ["path", "anchor", "text"],
                Optional = ["anchorEncoding", "textEncoding", "expectedFileHash", "expectedAnchorHash"],
                ExpectedAnchorHashTarget = "anchor",
            },
            new()
            {
                Kind = "delete_exact",
                Required = ["path", "oldText"],
                Optional = ["oldTextEncoding", "expectedFileHash", "expectedAnchorHash"],
                ExpectedAnchorHashTarget = "oldText",
            },
            new()
            {
                Kind = "replace_lines",
                Required = ["path", "startLine", "endLine", "oldRangeHash", "newText"],
                Optional = ["newTextEncoding", "expectedFileHash"],
            },
            new()
            {
                Kind = "json_set",
                Required = ["path", "pointer", "value"],
                Optional = ["expectedFileHash"],
            },
            new()
            {
                Kind = "replace_symbol_source",
                Required = ["newText", "oldSourceHash"],
                Optional = ["newTextEncoding", "symbolId", "name", "match", "kinds", "project", "includeNonPublic", "path", "line", "column", "expectedFileHash"],
            },
        ],
    };

    private static readonly IReadOnlyList<CommandCapabilityInfo> All = BuildAll();

    public static IReadOnlyList<CommandCapabilityInfo> GetAll() => All;

    public static CommandCapabilityInfo? Find(string commandName) =>
        All.FirstOrDefault(c =>
            string.Equals(c.Name, commandName, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<CommandCapabilityInfo> BuildAll() =>
    [
        new()
        {
            Name = CommandTypes.CurrentContext,
            Category = CategorySession,
            Description = "Return the active root, target, server identity, and supported protocol versions.",
            Parameters = [],
        },
        new()
        {
            Name = CommandTypes.ListRoots,
            Category = CategorySession,
            Description = "List all configured project roots; the active root is marked isCurrent.",
            Parameters = [],
        },
        new()
        {
            Name = CommandTypes.SetRoot,
            Category = CategorySession,
            Description = "Switch the active project root by name. The switch takes effect for the next request.",
            SideEffects = SideEffectsSession,
            Parameters =
            [
                new() { Name = "name", Type = "string", Required = true, Description = "Root name from list_roots; case-insensitive." },
            ],
        },
        new()
        {
            Name = CommandTypes.ListTargets,
            Category = CategorySession,
            Description = "List all configured chat-client targets; the receiving target is marked isCurrent.",
            Parameters = [],
        },
        new()
        {
            Name = CommandTypes.Capabilities,
            Category = CategorySession,
            Description = "Return structured descriptors for every registered command, or one descriptor when filtered by name.",
            Parameters =
            [
                new() { Name = "command", Type = "string", Required = false, Description = "Optional command name to return a single descriptor." },
            ],
        },
        new()
        {
            Name = CommandTypes.Tree,
            Category = CategoryFileSystem,
            Description = "Render a sandboxed directory tree as indented text.",
            Parameters =
            [
                new() { Name = "path", Type = "string", Required = false, Default = ".", Description = "Directory under the project root." },
                new() { Name = "depth", Type = "number", Required = false, Default = "3", Description = "Levels to descend." },
                new() { Name = "include", Type = "string[]", Required = false, Description = "File globs to include." },
                new() { Name = "exclude", Type = "string[]", Required = false, Description = "File globs to exclude, on top of defaults." },
            ],
        },
        new()
        {
            Name = CommandTypes.ReadFile,
            Category = CategoryFileSystem,
            Description = "Read text content from a file under the project root.",
            Parameters =
            [
                new() { Name = "path", Type = "string", Required = true, Description = "File path under the project root." },
                new() { Name = "startLine", Type = "number", Required = false, Description = "1-based inclusive first line." },
                new() { Name = "endLine", Type = "number", Required = false, Description = "1-based inclusive last line." },
                new() { Name = "maxBytes", Type = "number", Required = false, Default = "1048576", Description = "Maximum UTF-8 bytes to return." },
            ],
        },
        new()
        {
            Name = CommandTypes.SearchText,
            Category = CategoryFileSystem,
            Description = "Search file text for a literal or regex pattern.",
            Parameters =
            [
                new() { Name = "pattern", Type = "string", Required = true, Description = "Literal text by default; .NET regex when isRegex is true." },
                new() { Name = "isRegex", Type = "boolean", Required = false, Default = "false" },
                new() { Name = "ignoreCase", Type = "boolean", Required = false, Default = "true" },
                new() { Name = "path", Type = "string", Required = false, Default = ".", Description = "Directory subtree to search." },
                new() { Name = "include", Type = "string[]", Required = false },
                new() { Name = "exclude", Type = "string[]", Required = false },
                new() { Name = "maxResults", Type = "number", Required = false, Default = "500" },
            ],
        },
        new()
        {
            Name = CommandTypes.ListFiles,
            Category = CategoryFileSystem,
            Description = "List file paths under a sandboxed subtree.",
            Parameters =
            [
                new() { Name = "path", Type = "string", Required = false, Default = "." },
                new() { Name = "include", Type = "string[]", Required = false },
                new() { Name = "exclude", Type = "string[]", Required = false },
                new() { Name = "maxResults", Type = "number", Required = false, Default = "5000" },
            ],
        },
        new()
        {
            Name = CommandTypes.ProjectInfo,
            Category = CategoryFileSystem,
            Description = "Inventory of solutions, projects, test projects, SDK version, and git metadata.",
            Parameters = [],
        },
        new()
        {
            Name = CommandTypes.GitStatus,
            Category = CategoryPatching,
            Description = "Return git clean/dirty state for the active root before starting a patch transaction.",
            Parameters = [],
        },
        new()
        {
            Name = CommandTypes.ProposePatch,
            Category = CategoryPatching,
            SideEffects = SideEffectsFileSystem,
            Description = "Apply a structured full-file patch when git is clean, optionally run a solution build and tests, and accept or retain needs_revision state.",
            Parameters =
            [
                new() { Name = "title", Type = "string", Required = false },
                new() { Name = "description", Type = "string", Required = false },
                new() { Name = "commitMessage", Type = "string", Required = false },
                new() { Name = "files", Type = "object[]", Required = false, Description = "Full-file ops. Each file op has path, operation create|replace|delete, oldContentHash for replace/delete, newContent for create/replace, and optional newContentEncoding base64utf8|gzipbase64utf8. At least one of files or edits is required." },
                new() { Name = "edits", Type = "object[]", Required = false, Description = "Lightweight textual edits applied after files in array order. At least one of files or edits is required." },
                new() { Name = "build", Type = "object", Required = false, Default = "{ \"policy\": \"none\" }", Description = "policy none|solution; optional path, configuration, timeoutSeconds, treatWarningsAsErrors." },
                new() { Name = "tests", Type = "object", Required = false, Default = "{ \"policy\": \"none\" }", Description = "policy none|all|projects|filter; projects/filter support projects[] and filter." },
            ],
            Features =
            [
                PatchEditsFeature,
            ],
        },
        new()
        {
            Name = CommandTypes.CurrentPatch,
            Category = CategoryPatching,
            Description = "Return the active in-memory patch transaction, or patchStatus none.",
            Parameters = [],
        },
        new()
        {
            Name = CommandTypes.ValidatePatch,
            Category = CategoryPatching,
            Description = "Validate a propose-like or amend-like patch without applying files, running build/tests, staging, or creating patch metadata.",
            Parameters =
            [
                new() { Name = "patchId", Type = "string", Required = false, Description = "When present, validate as an amendment against the active needs_revision patch." },
                new() { Name = "baseRevision", Type = "number", Required = false, Description = "Required with patchId for amendment validation." },
                new() { Name = "files", Type = "object[]", Required = false, Description = "Full-file ops to validate. At least one of files or edits is required." },
                new() { Name = "edits", Type = "object[]", Required = false, Description = "Lightweight edits to compile and validate in memory. At least one of files or edits is required." },
                new() { Name = "build", Type = "object", Required = false, Description = "Build policy shape to validate; omitted means none in proposal mode or inherited policy in amendment mode." },
                new() { Name = "tests", Type = "object", Required = false, Description = "Test policy shape to validate; omitted means none in proposal mode or inherited policy in amendment mode." },
            ],
            Features =
            [
                PatchEditsFeature,
            ],
        },
        new()
        {
            Name = CommandTypes.AmendPatch,
            Category = CategoryPatching,
            SideEffects = SideEffectsFileSystem,
            Description = "Apply corrective full-file operations to an active needs_revision patch, re-run build/tests, and accept or keep needs_revision state.",
            Parameters =
            [
                new() { Name = "patchId", Type = "string", Required = true },
                new() { Name = "baseRevision", Type = "number", Required = true },
                new() { Name = "description", Type = "string", Required = false },
                new() { Name = "files", Type = "object[]", Required = false, Description = "Full-file ops. Each file op has path, operation create|replace|delete, oldContentHash for current replace/delete, newContent for create/replace, and optional newContentEncoding base64utf8|gzipbase64utf8. At least one of files or edits is required." },
                new() { Name = "edits", Type = "object[]", Required = false, Description = "Lightweight textual edits applied after files in array order. At least one of files or edits is required." },
                new() { Name = "build", Type = "object", Required = false, Description = "Optional override; defaults to previous patch build policy." },
                new() { Name = "tests", Type = "object", Required = false, Description = "Optional override; defaults to previous patch tests policy. Supports none|all|projects|filter." },
            ],
            Features =
            [
                PatchEditsFeature,
            ],
        },
        new()
        {
            Name = CommandTypes.RevertPatch,
            Category = CategoryPatching,
            SideEffects = SideEffectsFileSystem,
            Description = "Revert the active patch transaction to its base HEAD and clear patch state.",
            Parameters =
            [
                new() { Name = "patchId", Type = "string", Required = true },
            ],
        },
        new()
        {
            Name = CommandTypes.DocumentSymbols,
            Category = CategoryRoslyn,
            Description = "Syntax-only outline of a C# file: types, members, nested structure.",
            Parameters =
            [
                new() { Name = "path", Type = "string", Required = true, Description = "C# file path under the project root." },
                new() { Name = "includeNonPublic", Type = "boolean", Required = false, Default = "true" },
            ],
        },
        new()
        {
            Name = CommandTypes.FindSymbol,
            Category = CategoryRoslyn,
            Description = "Find declarations by name across the loaded workspace.",
            Parameters =
            [
                new() { Name = "name", Type = "string", Required = true },
                new() { Name = "match", Type = "string", Required = false, Default = "exact", Description = "exact | prefix | contains." },
                new() { Name = "kinds", Type = "string[]", Required = false, Description = "class, interface, struct, enum, delegate, method, property, field, event." },
                new() { Name = "project", Type = "string", Required = false, Description = "Project-name filter." },
                new() { Name = "includeNonPublic", Type = "boolean", Required = false, Default = "false" },
                new() { Name = "ignoreCase", Type = "boolean", Required = false, Default = "true" },
                new() { Name = "maxResults", Type = "number", Required = false, Default = "100" },
            ],
        },
        new()
        {
            Name = CommandTypes.FindReferences,
            Category = CategoryRoslyn,
            Description = "Find references to a symbol identified by DocumentationCommentId.",
            Parameters =
            [
                new() { Name = "symbolId", Type = "string", Required = true, Description = "DocumentationCommentId from find_symbol." },
                new() { Name = "includeDefinition", Type = "boolean", Required = false, Default = "false" },
                new() { Name = "kinds", Type = "string[]", Required = false, Description = "definition, call, read, write, type_usage, inheritance, attribute, other." },
                new() { Name = "maxResults", Type = "number", Required = false, Default = "500" },
            ],
        },
        new()
        {
            Name = CommandTypes.GotoDefinition,
            Category = CategoryRoslyn,
            Description = "Find the declaration at a source position.",
            Parameters =
            [
                new() { Name = "path", Type = "string", Required = true },
                new() { Name = "line", Type = "number", Required = true, Description = "1-based line." },
                new() { Name = "column", Type = "number", Required = true, Description = "1-based column." },
            ],
        },
        new()
        {
            Name = CommandTypes.FindImplementations,
            Category = CategoryRoslyn,
            Description = "Find source implementations of an interface or abstract member.",
            Parameters =
            [
                new() { Name = "symbolId", Type = "string", Required = true },
                new() { Name = "transitive", Type = "boolean", Required = false, Default = "false" },
                new() { Name = "includeAbstract", Type = "boolean", Required = false, Default = "false" },
                new() { Name = "maxResults", Type = "number", Required = false, Default = "100" },
            ],
        },
        new()
        {
            Name = CommandTypes.FindCallers,
            Category = CategoryRoslyn,
            Description = "Find call sites for a method or constructor.",
            Parameters =
            [
                new() { Name = "symbolId", Type = "string", Required = true },
                new() { Name = "maxResults", Type = "number", Required = false, Default = "500" },
            ],
        },
        new()
        {
            Name = CommandTypes.FindDerivedTypes,
            Category = CategoryRoslyn,
            Description = "Find derived classes or interfaces of a base type.",
            Parameters =
            [
                new() { Name = "symbolId", Type = "string", Required = true },
                new() { Name = "transitive", Type = "boolean", Required = false, Default = "false" },
                new() { Name = "includeAbstract", Type = "boolean", Required = false, Default = "true" },
                new() { Name = "maxResults", Type = "number", Required = false, Default = "100" },
            ],
        },
        new()
        {
            Name = CommandTypes.FindOverrides,
            Category = CategoryRoslyn,
            Description = "Find overrides of a virtual, abstract, or override member.",
            Parameters =
            [
                new() { Name = "symbolId", Type = "string", Required = false, Description = "Provide this or path/line/column." },
                new() { Name = "path", Type = "string", Required = false },
                new() { Name = "line", Type = "number", Required = false },
                new() { Name = "column", Type = "number", Required = false },
                new() { Name = "includeAbstract", Type = "boolean", Required = false, Default = "true" },
                new() { Name = "maxResults", Type = "number", Required = false, Default = "100" },
            ],
        },
        new()
        {
            Name = CommandTypes.GetSymbolInfo,
            Category = CategoryRoslyn,
            Description = "Detailed symbol metadata: attributes, XML doc, base types, interfaces, generic constraints.",
            Parameters =
            [
                new() { Name = "symbolId", Type = "string", Required = true },
            ],
        },
        new()
        {
            Name = CommandTypes.GetSymbolSource,
            Category = CategoryRoslyn,
            Description = "Resolve a symbol by DocumentationCommentId, unique name, or source position and return its declaration source block.",
            Parameters =
            [
                new() { Name = "symbolId", Type = "string", Required = false, Description = "Provide exactly one selector: symbolId, name, or path/line/column." },
                new() { Name = "name", Type = "string", Required = false, Description = "Unique symbol name. If multiple symbols match, use find_symbol and retry with symbolId." },
                new() { Name = "match", Type = "string", Required = false, Default = "exact" },
                new() { Name = "kinds", Type = "string[]", Required = false },
                new() { Name = "project", Type = "string", Required = false },
                new() { Name = "includeNonPublic", Type = "boolean", Required = false, Default = "true" },
                new() { Name = "path", Type = "string", Required = false },
                new() { Name = "line", Type = "number", Required = false },
                new() { Name = "column", Type = "number", Required = false },
                new() { Name = "maxLines", Type = "number", Required = false, Default = "400" },
                new() { Name = "maxBytes", Type = "number", Required = false, Default = "1048576" },
            ],
        },
        new()
        {
            Name = CommandTypes.SqlSchema,
            Category = CategorySql,
            Description = "Return available schema collections, tables, and columns for the active SQL root.",
            Parameters = [],
        },
        new()
        {
            Name = CommandTypes.SqlTables,
            Category = CategorySql,
            Description = "List tables and views for the active SQL root, optionally filtered by catalog and schema.",
            Parameters =
            [
                new() { Name = "catalog", Type = "string", Required = false },
                new() { Name = "schema", Type = "string", Required = false },
            ],
        },
        new()
        {
            Name = CommandTypes.SqlColumns,
            Category = CategorySql,
            Description = "List columns for a table in the active SQL root.",
            Parameters =
            [
                new() { Name = "table", Type = "string", Required = true },
                new() { Name = "catalog", Type = "string", Required = false },
                new() { Name = "schema", Type = "string", Required = false },
            ],
        },
        new()
        {
            Name = CommandTypes.SqlQuery,
            Category = CategorySql,
            Description = "Execute a read-only query and return one stateless result page.",
            Parameters =
            [
                new() { Name = "sql", Type = "string", Required = true },
                new() { Name = "offset", Type = "number", Required = false, Default = "0" },
                new() { Name = "limit", Type = "number", Required = false, Description = "Capped by the root's configured maximum rows." },
            ],
        },
    ];
}
