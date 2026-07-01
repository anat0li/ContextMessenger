namespace ContextMessenger.Protocol.Commands;

public static class CommandTypes
{
    public const string CurrentContext = "current_context";
    public const string ListRoots = "list_roots";
    public const string SetRoot = "set_root";
    public const string ListTargets = "list_targets";
    public const string Capabilities = "capabilities";
    public const string Tree = "tree";
    public const string ReadFile = "read_file";
    public const string SearchText = "search_text";
    public const string ListFiles = "list_files";
    public const string ProjectInfo = "project_info";
    public const string GitStatus = "git_status";
    public const string ProposePatch = "propose_patch";
    public const string AmendPatch = "amend_patch";
    public const string ValidatePatch = "validate_patch";
    public const string CurrentPatch = "current_patch";
    public const string RevertPatch = "revert_patch";
    public const string DocumentSymbols = "document_symbols";
    public const string FindSymbol = "find_symbol";
    public const string FindReferences = "find_references";
    public const string GotoDefinition = "goto_definition";
    public const string FindImplementations = "find_implementations";
    public const string FindCallers = "find_callers";
    public const string FindDerivedTypes = "find_derived_types";
    public const string FindOverrides = "find_overrides";
    public const string GetSymbolInfo = "get_symbol_info";
    public const string GetSymbolSource = "get_symbol_source";
    public const string SqlSchema = "sql_schema";
    public const string SqlTables = "sql_tables";
    public const string SqlColumns = "sql_columns";
    public const string SqlQuery = "sql_query";
}
