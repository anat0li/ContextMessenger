namespace ContextMessenger.Core.Roslyn;

public interface IRoslynNavigationService : IRoslynWorkspaceInvalidator
{
    string GetWorkspaceVersion();

    DocumentSymbolsResult GetDocumentSymbols(DocumentSymbolsQuery query);

    FindSymbolsResult FindSymbols(FindSymbolQuery query);

    FindReferencesResult FindReferences(FindReferencesQuery query);

    GotoDefinitionResult GotoDefinition(GotoDefinitionQuery query);

    FindImplementationsResult FindImplementations(FindImplementationsQuery query);

    FindCallersResult FindCallers(FindCallersQuery query);

    FindDerivedTypesResult FindDerivedTypes(FindDerivedTypesQuery query);

    FindOverridesResult FindOverrides(FindOverridesQuery query);

    SymbolInfoResult GetSymbolInfo(GetSymbolInfoQuery query);

    GetSymbolSourceResult GetSymbolSource(GetSymbolSourceQuery query);
}
