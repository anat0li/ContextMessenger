using ContextMessenger.Core.FileSystem;
using ContextMessenger.Core.Roslyn;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ContextMessenger.Roslyn;

public sealed class DocumentSymbolService : IRoslynNavigationService, IDisposable
{
    private static readonly object MsBuildGate = new();
    private static bool _msBuildRegistrationAttempted;
    private static string? _msBuildRegistrationError;
    private readonly string _root;
    private readonly object _workspaceGate = new();
    private MSBuildWorkspace? _workspace;
    private Solution? _solution;
    private string? _solutionPath;
    private string? _solutionSignature;
    private long _workspaceGeneration;
    private long _loadedWorkspaceGeneration;
    private bool _disposed;

    public DocumentSymbolService(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Root path must be non-empty.", nameof(rootPath));

        var full = Path.GetFullPath(rootPath);
        if (!Directory.Exists(full))
            throw new DirectoryNotFoundException($"Sandbox root does not exist: {full}");

        _root = TrimTrailingSeparator(full);
    }

    public DocumentSymbolsResult GetDocumentSymbols(DocumentSymbolsQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.RelativePath))
            throw new ArgumentException("Path must be non-empty.", nameof(query));

        var abs = ResolveAbsolute(query.RelativePath);
        if (!File.Exists(abs))
            throw new FileNotFoundException($"File not found: {query.RelativePath}", query.RelativePath);
        if (!IsSupportedCSharpFile(abs))
            throw new NotSupportedException($"document_symbols supports only .cs and .csx files: {query.RelativePath}");

        var text = File.ReadAllText(abs);
        var rel = ToRelative(abs);
        var tree = CSharpSyntaxTree.ParseText(text, path: rel);
        var root = tree.GetCompilationUnitRoot();
        var symbols = ExtractMemberSymbols(root.Members, query.IncludeNonPublic);

        return new DocumentSymbolsResult
        {
            Path = rel,
            Symbols = symbols,
        };
    }

    public string GetWorkspaceVersion()
    {
        EnsureMsBuildRegistered();
        var solutionPath = FindSolutionPath();
        var signature = ComputeSolutionSignature(solutionPath);
        long generation;
        lock (_workspaceGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            generation = _workspaceGeneration;
        }

        return ToWorkspaceVersion(signature, generation);
    }

    public FindSymbolsResult FindSymbols(FindSymbolQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.Name))
            throw new ArgumentException("name is required.", nameof(query));
        if (query.MaxResults <= 0)
            throw new ArgumentOutOfRangeException(nameof(query), "maxResults must be positive.");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var solution = GetSolution(cts.Token);
        var comparison = query.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var results = new List<SymbolSummary>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in solution.Projects)
        {
            if (!string.IsNullOrWhiteSpace(query.Project) &&
                !string.Equals(project.Name, query.Project, StringComparison.Ordinal))
            {
                continue;
            }

            var symbols = FindDeclarations(project, query, comparison, cts.Token);

            foreach (var symbol in symbols)
            {
                if (!MatchesSymbolName(symbol.Name, query.Name, query.Match, comparison))
                    continue;
                if (!MatchesKind(symbol, query.Kinds))
                    continue;
                if (!query.IncludeNonPublic && symbol.DeclaredAccessibility != Accessibility.Public)
                    continue;

                var summary = ToSummary(solution, symbol);
                if (summary is null)
                    continue;
                if (!seen.Add(SummaryIdentity(summary)))
                    continue;

                results.Add(summary);
                if (results.Count >= query.MaxResults)
                    return new FindSymbolsResult
                    {
                        WorkspaceVersion = WorkspaceVersion(),
                        Matches = SortSummaries(results),
                    };
            }
        }

        return new FindSymbolsResult
        {
            WorkspaceVersion = WorkspaceVersion(),
            Matches = SortSummaries(results),
        };
    }

    public FindReferencesResult FindReferences(FindReferencesQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.SymbolId))
            throw new ArgumentException("symbolId is required.", nameof(query));
        if (query.MaxResults <= 0)
            throw new ArgumentOutOfRangeException(nameof(query), "maxResults must be positive.");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var solution = GetSolution(cts.Token);
        var symbol = ResolveSymbol(solution, query.SymbolId, cts.Token);
        if (symbol is null)
            throw new SymbolNotFoundException(query.SymbolId);

        return new FindReferencesResult
        {
            WorkspaceVersion = WorkspaceVersion(),
            Symbol = ToSummary(solution, symbol),
            References = CollectReferences(
                solution,
                symbol,
                query.SymbolId,
                query.IncludeDefinition,
                query.Kinds,
                query.MaxResults,
                cts.Token),
        };
    }

    public GotoDefinitionResult GotoDefinition(GotoDefinitionQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.RelativePath))
            throw new ArgumentException("path is required.", nameof(query));
        if (query.Line <= 0)
            throw new ArgumentOutOfRangeException(nameof(query), "line must be 1-based.");
        if (query.Column <= 0)
            throw new ArgumentOutOfRangeException(nameof(query), "column must be 1-based.");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var solution = GetSolution(cts.Token);
        var symbol = ResolveSymbolAtLocation(solution, query.RelativePath, query.Line, query.Column, cts.Token);

        if (symbol is null)
        {
            return new GotoDefinitionResult
            {
                WorkspaceVersion = WorkspaceVersion(),
                Definitions = [],
            };
        }

        return new GotoDefinitionResult
        {
            WorkspaceVersion = WorkspaceVersion(),
            Definitions = symbol.OriginalDefinition.Locations
                .Where(location => location.IsInSource)
                .Select(location => ToSummary(solution, symbol.OriginalDefinition, location))
                .Where(summary => summary is not null)
                .Select(summary => summary!)
                .ToArray(),
        };
    }

    public FindImplementationsResult FindImplementations(FindImplementationsQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.SymbolId))
            throw new ArgumentException("symbolId is required.", nameof(query));
        if (query.MaxResults <= 0)
            throw new ArgumentOutOfRangeException(nameof(query), "maxResults must be positive.");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var solution = GetSolution(cts.Token);
        var symbol = ResolveSymbol(solution, query.SymbolId, cts.Token);
        if (symbol is null)
            throw new SymbolNotFoundException(query.SymbolId);

        var implementationSymbols = symbol is INamedTypeSymbol namedType
            ? SymbolFinder.FindImplementationsAsync(
                    namedType,
                    solution,
                    query.Transitive,
                    projects: null,
                    cts.Token)
                .GetAwaiter()
                .GetResult()
            : SymbolFinder.FindImplementationsAsync(
                    symbol,
                    solution,
                    projects: null,
                    cts.Token)
                .GetAwaiter()
                .GetResult();

        var summaries = implementationSymbols
            .Where(symbol => query.IncludeAbstract || !IsAbstractSymbol(symbol))
            .Select(symbol => ToSummary(solution, symbol.OriginalDefinition))
            .Where(summary => summary is not null)
            .Select(summary => summary!)
            .GroupBy(SummaryIdentity, StringComparer.Ordinal)
            .Select(group => group.First())
            .Take(query.MaxResults);

        return new FindImplementationsResult
        {
            WorkspaceVersion = WorkspaceVersion(),
            Symbol = ToSummary(solution, symbol),
            Implementations = SortSummaries(summaries),
        };
    }

    public FindCallersResult FindCallers(FindCallersQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.SymbolId))
            throw new ArgumentException("symbolId is required.", nameof(query));
        if (query.MaxResults <= 0)
            throw new ArgumentOutOfRangeException(nameof(query), "maxResults must be positive.");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var solution = GetSolution(cts.Token);
        var symbol = ResolveSymbol(solution, query.SymbolId, cts.Token);
        if (symbol is null)
            throw new SymbolNotFoundException(query.SymbolId);

        return new FindCallersResult
        {
            WorkspaceVersion = WorkspaceVersion(),
            Symbol = ToSummary(solution, symbol),
            Callers = CollectReferences(
                solution,
                symbol,
                query.SymbolId,
                includeDefinition: false,
                kinds: ["call"],
                query.MaxResults,
                cts.Token),
        };
    }

    public FindDerivedTypesResult FindDerivedTypes(FindDerivedTypesQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.SymbolId))
            throw new ArgumentException("symbolId is required.", nameof(query));
        if (query.MaxResults <= 0)
            throw new ArgumentOutOfRangeException(nameof(query), "maxResults must be positive.");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var solution = GetSolution(cts.Token);
        var symbol = ResolveSymbol(solution, query.SymbolId, cts.Token);
        if (symbol is null)
            throw new SymbolNotFoundException(query.SymbolId);
        if (symbol is not INamedTypeSymbol namedType)
            return new FindDerivedTypesResult { Symbol = ToSummary(solution, symbol), DerivedTypes = [] };

        IEnumerable<INamedTypeSymbol> derived = namedType.TypeKind == TypeKind.Interface
            ? SymbolFinder.FindDerivedInterfacesAsync(
                    namedType,
                    solution,
                    query.Transitive,
                    projects: null,
                    cts.Token)
                .GetAwaiter()
                .GetResult()
            : SymbolFinder.FindDerivedClassesAsync(
                    namedType,
                    solution,
                    query.Transitive,
                    projects: null,
                    cts.Token)
                .GetAwaiter()
                .GetResult();

        var summaries = derived
            .Where(symbol => query.IncludeAbstract || !symbol.IsAbstract)
            .Select(symbol => ToSummary(solution, symbol.OriginalDefinition))
            .Where(summary => summary is not null)
            .Select(summary => summary!)
            .GroupBy(SummaryIdentity, StringComparer.Ordinal)
            .Select(group => group.First())
            .Take(query.MaxResults);

        return new FindDerivedTypesResult
        {
            WorkspaceVersion = WorkspaceVersion(),
            Symbol = ToSummary(solution, namedType),
            DerivedTypes = SortSummaries(summaries),
        };
    }

    public FindOverridesResult FindOverrides(FindOverridesQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var hasSymbolId = !string.IsNullOrWhiteSpace(query.SymbolId);
        var hasLocation = !string.IsNullOrWhiteSpace(query.RelativePath) ||
                          query.Line is not null ||
                          query.Column is not null;
        if (!hasSymbolId && !hasLocation)
            throw new ArgumentException("symbolId or path/line/column is required.", nameof(query));
        if (hasSymbolId && hasLocation)
            throw new ArgumentException("Provide either symbolId or path/line/column, not both.", nameof(query));
        if (hasLocation && (string.IsNullOrWhiteSpace(query.RelativePath) || query.Line is null || query.Column is null))
            throw new ArgumentException("path, line, and column are required together.", nameof(query));
        if (query.MaxResults <= 0)
            throw new ArgumentOutOfRangeException(nameof(query), "maxResults must be positive.");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var solution = GetSolution(cts.Token);
        var symbol = hasSymbolId
            ? ResolveSymbol(solution, query.SymbolId!, cts.Token)
            : ResolveSymbolAtLocation(solution, query.RelativePath!, query.Line!.Value, query.Column!.Value, cts.Token);
        if (symbol is null)
            throw new SymbolNotFoundException(hasSymbolId ? query.SymbolId! : $"{query.RelativePath}:{query.Line}:{query.Column}");

        if (symbol is not IMethodSymbol and not IPropertySymbol and not IEventSymbol)
        {
            return new FindOverridesResult
            {
                WorkspaceVersion = WorkspaceVersion(),
                Symbol = ToSummary(solution, symbol),
                Overrides = [],
            };
        }

        var overrideSymbols = SymbolFinder.FindOverridesAsync(
                symbol,
                solution,
                projects: null,
                cts.Token)
            .GetAwaiter()
            .GetResult();

        var summaries = overrideSymbols
            .Where(symbol => query.IncludeAbstract || !IsAbstractSymbol(symbol))
            .Select(symbol => ToSummary(solution, symbol.OriginalDefinition))
            .Where(summary => summary is not null)
            .Select(summary => summary!)
            .GroupBy(SummaryIdentity, StringComparer.Ordinal)
            .Select(group => group.First())
            .Take(query.MaxResults);

        return new FindOverridesResult
        {
            WorkspaceVersion = WorkspaceVersion(),
            Symbol = ToSummary(solution, symbol),
            Overrides = SortSummaries(summaries),
        };
    }

    public SymbolInfoResult GetSymbolInfo(GetSymbolInfoQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.SymbolId))
            throw new ArgumentException("symbolId is required.", nameof(query));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var solution = GetSolution(cts.Token);
        var symbol = ResolveSymbol(solution, query.SymbolId, cts.Token);
        if (symbol is null)
            throw new SymbolNotFoundException(query.SymbolId);

        return new SymbolInfoResult
        {
            WorkspaceVersion = WorkspaceVersion(),
            Symbol = ToSummary(solution, symbol),
            DocumentationXml = EmptyToNull(symbol.GetDocumentationCommentXml(cancellationToken: cts.Token)),
            Attributes = SymbolAttributes(symbol),
            BaseTypes = SymbolBaseTypes(symbol),
            ImplementedInterfaces = SymbolImplementedInterfaces(symbol),
            TypeParameters = SymbolTypeParameters(symbol),
            GenericConstraints = SymbolGenericConstraints(symbol),
            ReturnType = SymbolReturnType(symbol),
            Parameters = SymbolParameters(symbol),
            IsAsync = symbol is IMethodSymbol method ? method.IsAsync : null,
            IsStatic = symbol is IMethodSymbol ? symbol.IsStatic : null,
            IsAbstract = symbol is IMethodSymbol ? symbol.IsAbstract : null,
            IsVirtual = symbol is IMethodSymbol ? symbol.IsVirtual : null,
            IsOverride = symbol is IMethodSymbol ? symbol.IsOverride : null,
            OverriddenMethod = SymbolOverriddenMethod(symbol),
            ImplementedInterfaceMembers = SymbolImplementedInterfaceMembers(symbol),
        };
    }

    public GetSymbolSourceResult GetSymbolSource(GetSymbolSourceQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.MaxLines <= 0)
            throw new ArgumentOutOfRangeException(nameof(query), "maxLines must be positive.");
        if (query.MaxBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(query), "maxBytes must be positive.");

        var hasSymbolId = !string.IsNullOrWhiteSpace(query.SymbolId);
        var hasName = !string.IsNullOrWhiteSpace(query.Name);
        var hasLocation = !string.IsNullOrWhiteSpace(query.RelativePath) ||
                          query.Line is not null ||
                          query.Column is not null;
        var selectorCount = (hasSymbolId ? 1 : 0) + (hasName ? 1 : 0) + (hasLocation ? 1 : 0);
        if (selectorCount == 0)
            throw new ArgumentException("symbolId, name, or path/line/column is required.", nameof(query));
        if (selectorCount > 1)
            throw new ArgumentException("Provide exactly one selector: symbolId, name, or path/line/column.", nameof(query));
        if (hasLocation && (string.IsNullOrWhiteSpace(query.RelativePath) || query.Line is null || query.Column is null))
            throw new ArgumentException("path, line, and column are required together.", nameof(query));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var solution = GetSolution(cts.Token);
        var symbol = hasSymbolId
            ? ResolveSymbol(solution, query.SymbolId!, cts.Token)
            : hasName
                ? ResolveSingleSymbolByName(solution, query, cts.Token)
            : ResolveSymbolAtLocation(solution, query.RelativePath!, query.Line!.Value, query.Column!.Value, cts.Token);
        if (symbol is null)
            throw new SymbolNotFoundException(hasSymbolId ? query.SymbolId! : hasName ? query.Name! : $"{query.RelativePath}:{query.Line}:{query.Column}");

        var summary = ToSummary(solution, symbol)
            ?? throw new InvalidOperationException("Resolved symbol does not have a source location.");
        var source = GetDeclarationSource(solution, symbol, query.MaxLines, query.MaxBytes, cts.Token);

        return new GetSymbolSourceResult
        {
            WorkspaceVersion = WorkspaceVersion(),
            Symbol = summary,
            Source = source,
        };
    }

    private ISymbol? ResolveSingleSymbolByName(
        Solution solution,
        GetSymbolSourceQuery query,
        CancellationToken cancellationToken)
    {
        var result = FindSymbols(new FindSymbolQuery
        {
            Name = query.Name!,
            Match = query.Match,
            Kinds = query.Kinds,
            Project = query.Project,
            IncludeNonPublic = query.IncludeNonPublic,
            MaxResults = 2,
        });

        if (result.Matches.Count == 0)
            return null;
        if (result.Matches.Count > 1)
        {
            throw new ArgumentException(
                $"Name '{query.Name}' matched multiple symbols. Use find_symbol to choose a symbolId, then call get_symbol_source with symbolId.",
                nameof(query));
        }

        return ResolveSymbol(solution, result.Matches[0].SymbolId, cancellationToken);
    }

    public void Dispose()
    {
        lock (_workspaceGate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _workspace?.Dispose();
            _workspace = null;
            _solution = null;
        }
    }

    public void InvalidateWorkspace()
    {
        lock (_workspaceGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _workspaceGeneration++;
        }
    }

    private static IReadOnlyList<DocumentSymbol> ExtractMemberSymbols(
        SyntaxList<MemberDeclarationSyntax> members,
        bool includeNonPublic)
    {
        var symbols = new List<DocumentSymbol>();
        foreach (var member in members)
        {
            if (member is BaseNamespaceDeclarationSyntax ns)
            {
                symbols.AddRange(ExtractMemberSymbols(ns.Members, includeNonPublic));
                continue;
            }

            var symbol = CreateSymbol(member, includeNonPublic);
            if (symbol is not null)
                symbols.Add(symbol);
        }

        return symbols;
    }

    private static DocumentSymbol? CreateSymbol(MemberDeclarationSyntax member, bool includeNonPublic)
    {
        if (!includeNonPublic && !IsPublic(member))
            return null;

        return member switch
        {
            ClassDeclarationSyntax declaration => TypeSymbol(declaration, "class", includeNonPublic),
            InterfaceDeclarationSyntax declaration => TypeSymbol(declaration, "interface", includeNonPublic),
            StructDeclarationSyntax declaration => TypeSymbol(declaration, "struct", includeNonPublic),
            RecordDeclarationSyntax declaration => TypeSymbol(declaration, declaration.ClassOrStructKeyword.Text == "struct" ? "record_struct" : "record", includeNonPublic),
            EnumDeclarationSyntax declaration => EnumSymbol(declaration),
            DelegateDeclarationSyntax declaration => LeafSymbol(
                declaration.Identifier.ValueText,
                "delegate",
                declaration,
                SignatureWithoutBody(declaration)),
            MethodDeclarationSyntax declaration => LeafSymbol(
                declaration.Identifier.ValueText,
                "method",
                declaration,
                $"{Modifiers(declaration.Modifiers)}{declaration.ReturnType} {declaration.Identifier}{declaration.TypeParameterList}{declaration.ParameterList}".Trim()),
            ConstructorDeclarationSyntax declaration => LeafSymbol(
                declaration.Identifier.ValueText,
                "constructor",
                declaration,
                $"{Modifiers(declaration.Modifiers)}{declaration.Identifier}{declaration.ParameterList}".Trim()),
            PropertyDeclarationSyntax declaration => LeafSymbol(
                declaration.Identifier.ValueText,
                "property",
                declaration,
                $"{Modifiers(declaration.Modifiers)}{declaration.Type} {declaration.Identifier}".Trim()),
            EventDeclarationSyntax declaration => LeafSymbol(
                declaration.Identifier.ValueText,
                "event",
                declaration,
                $"{Modifiers(declaration.Modifiers)}event {declaration.Type} {declaration.Identifier}".Trim()),
            EventFieldDeclarationSyntax declaration => LeafSymbol(
                string.Join(", ", declaration.Declaration.Variables.Select(v => v.Identifier.ValueText)),
                "event",
                declaration,
                $"{Modifiers(declaration.Modifiers)}event {declaration.Declaration.Type} {string.Join(", ", declaration.Declaration.Variables.Select(v => v.Identifier.ValueText))}".Trim()),
            FieldDeclarationSyntax declaration => LeafSymbol(
                string.Join(", ", declaration.Declaration.Variables.Select(v => v.Identifier.ValueText)),
                "field",
                declaration,
                $"{Modifiers(declaration.Modifiers)}{declaration.Declaration.Type} {string.Join(", ", declaration.Declaration.Variables.Select(v => v.Identifier.ValueText))}".Trim()),
            _ => null,
        };
    }

    private static DocumentSymbol TypeSymbol(TypeDeclarationSyntax declaration, string kind, bool includeNonPublic) =>
        LeafSymbol(
            declaration.Identifier.ValueText,
            kind,
            declaration,
            $"{Modifiers(declaration.Modifiers)}{declaration.Keyword} {declaration.Identifier}{declaration.TypeParameterList}".Trim(),
            ExtractMemberSymbols(declaration.Members, includeNonPublic));

    private static DocumentSymbol EnumSymbol(EnumDeclarationSyntax declaration) =>
        LeafSymbol(
            declaration.Identifier.ValueText,
            "enum",
            declaration,
            $"{Modifiers(declaration.Modifiers)}enum {declaration.Identifier}".Trim(),
            declaration.Members.Select(member => LeafSymbol(
                member.Identifier.ValueText,
                "enum_member",
                member,
                member.Identifier.ValueText)).ToArray());

    private static DocumentSymbol LeafSymbol(
        string name,
        string kind,
        SyntaxNode node,
        string? signature,
        IReadOnlyList<DocumentSymbol>? children = null)
    {
        var span = node.GetLocation().GetLineSpan().StartLinePosition;
        var end = node.GetLocation().GetLineSpan().EndLinePosition;
        return new DocumentSymbol
        {
            Name = name,
            Kind = kind,
            Line = span.Line + 1,
            EndLine = end.Line + 1,
            Signature = string.IsNullOrWhiteSpace(signature) ? null : NormalizeSignature(signature),
            Children = children ?? [],
        };
    }

    private static bool IsPublic(MemberDeclarationSyntax declaration)
    {
        if (declaration is BaseNamespaceDeclarationSyntax)
            return true;

        return declaration.Modifiers.Any(SyntaxKind.PublicKeyword);
    }

    private static string Modifiers(SyntaxTokenList modifiers) =>
        modifiers.Count == 0 ? "" : string.Join(" ", modifiers.Select(m => m.Text)) + " ";

    private static string SignatureWithoutBody(CSharpSyntaxNode node)
    {
        var text = node switch
        {
            DelegateDeclarationSyntax del => $"{Modifiers(del.Modifiers)}delegate {del.ReturnType} {del.Identifier}{del.TypeParameterList}{del.ParameterList}",
            _ => node.ToString(),
        };
        return text.Trim();
    }

    private static string NormalizeSignature(string value) =>
        string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Replace("( ", "(", StringComparison.Ordinal)
            .Replace(" )", ")", StringComparison.Ordinal)
            .Replace("[ ", "[", StringComparison.Ordinal)
            .Replace(" ]", "]", StringComparison.Ordinal);

    private static bool IsSupportedCSharpFile(string absolutePath)
    {
        var extension = Path.GetExtension(absolutePath);
        return string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".csx", StringComparison.OrdinalIgnoreCase);
    }

    private Solution GetSolution(CancellationToken cancellationToken)
    {
        EnsureMsBuildRegistered();
        var solutionPath = FindSolutionPath();
        var signature = ComputeSolutionSignature(solutionPath);

        lock (_workspaceGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_solution is not null &&
                string.Equals(_solutionPath, solutionPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(_solutionSignature, signature, StringComparison.Ordinal) &&
                _loadedWorkspaceGeneration == _workspaceGeneration)
            {
                return _solution;
            }

            _workspace?.Dispose();
            _workspace = MSBuildWorkspace.Create();
            _solution = _workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken)
                .GetAwaiter()
                .GetResult();
            _solutionPath = solutionPath;
            _solutionSignature = signature;
            _loadedWorkspaceGeneration = _workspaceGeneration;
            return _solution;
        }
    }

    private static void EnsureMsBuildRegistered()
    {
        lock (MsBuildGate)
        {
            if (_msBuildRegistrationAttempted)
            {
                if (_msBuildRegistrationError is not null)
                    throw new InvalidOperationException($"Roslyn workspace unavailable: {_msBuildRegistrationError}");
                return;
            }

            _msBuildRegistrationAttempted = true;
            try
            {
                if (!MSBuildLocator.IsRegistered)
                    MSBuildLocator.RegisterDefaults();
            }
            catch (Exception ex)
            {
                _msBuildRegistrationError = ex.Message;
                throw new InvalidOperationException($"Roslyn workspace unavailable: {_msBuildRegistrationError}", ex);
            }
        }
    }

    private string FindSolutionPath()
    {
        var solutions = Directory.GetFiles(_root, "*.sln")
            .Concat(Directory.GetFiles(_root, "*.slnx"))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (solutions.Length == 0)
            throw new FileNotFoundException("No .sln or .slnx file found under the selected root.");

        return solutions[0];
    }

    private string ComputeSolutionSignature(string solutionPath)
    {
        var values = new List<string> { $"{solutionPath}:{File.GetLastWriteTimeUtc(solutionPath).Ticks}" };
        foreach (var csproj in Directory.GetFiles(_root, "*.csproj", SearchOption.AllDirectories)
                     .Where(path => !PathContainsExcludedDirectory(path))
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            values.Add($"{csproj}:{File.GetLastWriteTimeUtc(csproj).Ticks}");
        }

        return string.Join("|", values);
    }

    private string WorkspaceVersion()
    {
        string? signature;
        long generation;
        lock (_workspaceGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            signature = _solutionSignature;
            generation = _workspaceGeneration;
        }

        if (string.IsNullOrWhiteSpace(signature))
            return GetWorkspaceVersion();

        return ToWorkspaceVersion(signature, generation);
    }

    private static string ToWorkspaceVersion(string signature, long generation)
    {
        var bytes = global::System.Text.Encoding.UTF8.GetBytes($"{generation}|{signature}");
        var hash = SHA256.HashData(bytes);
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static bool PathContainsExcludedDirectory(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part =>
            string.Equals(part, "bin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(part, "obj", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(part, ".git", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(part, ".vs", StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesSymbolName(string symbolName, string queryName, string match, StringComparison comparison) =>
        match.ToLowerInvariant() switch
        {
            "prefix" => symbolName.StartsWith(queryName, comparison),
            "contains" => symbolName.Contains(queryName, comparison),
            _ => string.Equals(symbolName, queryName, comparison),
        };

    private static IEnumerable<ISymbol> FindDeclarations(
        Project project,
        FindSymbolQuery query,
        StringComparison comparison,
        CancellationToken cancellationToken)
    {
        if (string.Equals(query.Match, "exact", StringComparison.OrdinalIgnoreCase))
        {
            return SymbolFinder.FindDeclarationsAsync(
                    project,
                    query.Name,
                    query.IgnoreCase,
                    SymbolFilter.TypeAndMember,
                    cancellationToken)
                .GetAwaiter()
                .GetResult();
        }

        var compilation = project.GetCompilationAsync(cancellationToken).GetAwaiter().GetResult();
        if (compilation is null)
            return [];

        return EnumerateSymbols(compilation.GlobalNamespace)
            .Where(symbol => MatchesSymbolName(symbol.Name, query.Name, query.Match, comparison))
            .ToArray();
    }

    private static IEnumerable<ISymbol> EnumerateSymbols(INamespaceOrTypeSymbol container)
    {
        foreach (var member in container.GetMembers())
        {
            if (member is INamespaceOrTypeSymbol nested)
            {
                if (member is INamedTypeSymbol)
                    yield return member;

                foreach (var child in EnumerateSymbols(nested))
                    yield return child;

                continue;
            }

            if (member is IMethodSymbol or IPropertySymbol or IFieldSymbol or IEventSymbol)
                yield return member;
        }
    }

    private static bool MatchesKind(ISymbol symbol, IReadOnlyList<string> kinds)
    {
        if (kinds.Count == 0)
            return true;

        var kind = ToKind(symbol);
        return kinds.Any(item => string.Equals(item, kind, StringComparison.OrdinalIgnoreCase));
    }

    private IReadOnlyList<ContextMessenger.Core.Roslyn.ReferenceLocation> CollectReferences(
        Solution solution,
        ISymbol symbol,
        string requestedSymbolId,
        bool includeDefinition,
        IReadOnlyList<string> kinds,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var filter = new HashSet<string>(
            kinds.Where(kind => !string.IsNullOrWhiteSpace(kind)),
            StringComparer.OrdinalIgnoreCase);
        var locations = new List<ContextMessenger.Core.Roslyn.ReferenceLocation>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (includeDefinition)
        {
            foreach (var definition in symbol.Locations.Where(location => location.IsInSource))
            {
                AddReferenceIfIncluded(
                    solution,
                    requestedSymbolId,
                    definition,
                    isDefinition: true,
                    filter,
                    locations,
                    seen,
                    cancellationToken);

                if (locations.Count >= maxResults)
                    return SortReferenceLocations(locations);
            }
        }

        var references = SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken: cancellationToken)
            .GetAwaiter()
            .GetResult();

        foreach (var referencedSymbol in references)
        {
            foreach (var location in referencedSymbol.Locations)
            {
                if (location.IsImplicit)
                    continue;

                AddReferenceIfIncluded(
                    solution,
                    requestedSymbolId,
                    location.Location,
                    isDefinition: false,
                    filter,
                    locations,
                    seen,
                    cancellationToken);

                if (locations.Count >= maxResults)
                    return SortReferenceLocations(locations);
            }
        }

        return SortReferenceLocations(locations);
    }

    private void AddReferenceIfIncluded(
        Solution solution,
        string requestedSymbolId,
        Location location,
        bool isDefinition,
        HashSet<string> filter,
        List<ContextMessenger.Core.Roslyn.ReferenceLocation> locations,
        HashSet<string> seen,
        CancellationToken cancellationToken)
    {
        var kind = ClassifyReference(location, isDefinition, cancellationToken);
        if (filter.Count > 0 && !filter.Contains(kind))
            return;

        var reference = ToReferenceLocation(solution, requestedSymbolId, location, isDefinition, kind);
        if (reference is null)
            return;

        var key = $"{reference.Path}|{reference.Line}|{reference.Column}|{reference.Kind}|{reference.LineText}";
        if (!seen.Add(key))
            return;

        locations.Add(reference);
    }

    private static IReadOnlyList<ContextMessenger.Core.Roslyn.ReferenceLocation> SortReferenceLocations(
        IEnumerable<ContextMessenger.Core.Roslyn.ReferenceLocation> locations) =>
        locations
            .OrderBy(location => location.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(location => location.Line)
            .ThenBy(location => location.Column)
            .ToArray();

    private static bool IsAbstractSymbol(ISymbol symbol) =>
        symbol switch
        {
            INamedTypeSymbol named => named.IsAbstract,
            IMethodSymbol method => method.IsAbstract || method.ContainingType?.IsAbstract == true,
            IPropertySymbol property => property.IsAbstract || property.ContainingType?.IsAbstract == true,
            IEventSymbol evt => evt.IsAbstract || evt.ContainingType?.IsAbstract == true,
            _ => false,
        };

    private static string ClassifyReference(Location location, bool isDefinition, CancellationToken cancellationToken)
    {
        if (isDefinition)
            return "definition";
        if (!location.IsInSource || location.SourceTree is null)
            return "other";

        var root = location.SourceTree.GetRoot(cancellationToken);
        var node = root.FindNode(location.SourceSpan, getInnermostNodeForTie: true);
        if (node is null)
            return "other";

        if (node.AncestorsAndSelf().Any(ancestor =>
                ancestor is InvocationExpressionSyntax ||
                ancestor is ObjectCreationExpressionSyntax ||
                ancestor is ConstructorInitializerSyntax))
        {
            return "call";
        }

        if (node.AncestorsAndSelf().Any(ancestor =>
                ancestor is AttributeSyntax ||
                ancestor is AttributeListSyntax))
        {
            return "attribute";
        }

        if (node.AncestorsAndSelf().Any(ancestor =>
                ancestor is BaseListSyntax ||
                ancestor is TypeConstraintSyntax))
        {
            return "inheritance";
        }

        if (node.AncestorsAndSelf().Any(ancestor =>
                ancestor is AssignmentExpressionSyntax assignment &&
                assignment.Left.Span.Contains(location.SourceSpan.Start)))
        {
            return "write";
        }

        if (node.AncestorsAndSelf().Any(ancestor =>
                ancestor is TypeSyntax ||
                ancestor is ObjectCreationExpressionSyntax))
        {
            return "type_usage";
        }

        if (node.AncestorsAndSelf().Any(ancestor =>
                ancestor is AssignmentExpressionSyntax ||
                ancestor is ArgumentSyntax ||
                ancestor is ReturnStatementSyntax ||
                ancestor is ArrowExpressionClauseSyntax ||
                ancestor is EqualsValueClauseSyntax))
        {
            return "read";
        }

        return "other";
    }

    private static ISymbol? ResolveSymbol(Solution solution, string symbolId, CancellationToken cancellationToken)
    {
        var compilations = solution.Projects
            .Select(project => project.GetCompilationAsync(cancellationToken).GetAwaiter().GetResult())
            .Where(compilation => compilation is not null)
            .Select(compilation => compilation!)
            .ToArray();

        foreach (var candidate in CandidateSymbolIds(symbolId))
        {
            foreach (var compilation in compilations)
            {
                var symbol = DocumentationCommentId.GetFirstSymbolForDeclarationId(candidate, compilation);
                if (symbol is not null)
                    return symbol;
            }
        }

        return ResolveStrippedGenericTypeSymbol(compilations, symbolId);
    }

    private ISymbol? ResolveSymbolAtLocation(
        Solution solution,
        string relativePath,
        int lineNumber,
        int columnNumber,
        CancellationToken cancellationToken)
    {
        var abs = ResolveAbsolute(relativePath);
        var document = solution.Projects
            .SelectMany(project => project.Documents)
            .FirstOrDefault(document =>
                string.Equals(document.FilePath, abs, StringComparison.OrdinalIgnoreCase));
        if (document is null)
            throw new FileNotFoundException($"File not found in loaded solution: {relativePath}", relativePath);

        var text = document.GetTextAsync(cancellationToken).GetAwaiter().GetResult();
        if (lineNumber <= 0)
            throw new ArgumentOutOfRangeException(nameof(lineNumber), "line must be 1-based.");
        if (columnNumber <= 0)
            throw new ArgumentOutOfRangeException(nameof(columnNumber), "column must be 1-based.");
        if (lineNumber > text.Lines.Count)
            throw new ArgumentOutOfRangeException(nameof(lineNumber), "line is outside the file.");

        var line = text.Lines[lineNumber - 1];
        var position = line.Start + Math.Min(columnNumber - 1, Math.Max(0, line.End - line.Start));
        var root = document.GetSyntaxRootAsync(cancellationToken).GetAwaiter().GetResult();
        var model = document.GetSemanticModelAsync(cancellationToken).GetAwaiter().GetResult();
        if (root is null || model is null)
            return null;

        var token = root.FindToken(position);
        var node = token.Parent;
        var symbol = node is null
            ? null
            : model.GetSymbolInfo(node, cancellationToken).Symbol ??
              model.GetDeclaredSymbol(node, cancellationToken);
        if (symbol is not null)
            return symbol.OriginalDefinition;

        if (node is null)
            return null;

        foreach (var ancestor in node.Ancestors())
        {
            symbol = model.GetSymbolInfo(ancestor, cancellationToken).Symbol ??
                     model.GetDeclaredSymbol(ancestor, cancellationToken);
            if (symbol is not null)
                return symbol.OriginalDefinition;
        }

        return null;
    }

    private SymbolSourceBlock GetDeclarationSource(
        Solution solution,
        ISymbol symbol,
        int maxLines,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var location = symbol.OriginalDefinition.Locations.FirstOrDefault(location => location.IsInSource)
            ?? throw new InvalidOperationException("Resolved symbol does not have a source location.");
        if (location.SourceTree is null)
            throw new InvalidOperationException("Resolved symbol source location does not have a syntax tree.");

        var document = solution.GetDocument(location.SourceTree)
            ?? throw new InvalidOperationException("Resolved symbol source document was not found in the loaded solution.");
        var text = document.GetTextAsync(cancellationToken).GetAwaiter().GetResult();
        var root = document.GetSyntaxRootAsync(cancellationToken).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("Resolved symbol source document does not have a syntax root.");
        var model = document.GetSemanticModelAsync(cancellationToken).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("Resolved symbol source document does not have a semantic model.");

        var declaration = FindDeclarationNode(root, model, symbol.OriginalDefinition, location.SourceSpan, cancellationToken)
            ?? throw new InvalidOperationException("Resolved symbol declaration syntax was not found.");
        var span = TrimLeadingBlankLines(text, declaration.FullSpan);
        var start = text.Lines.GetLinePosition(span.Start);
        var end = text.Lines.GetLinePosition(span.End);
        var lineCount = end.Line - start.Line + 1;
        if (lineCount > maxLines)
            throw new ArgumentOutOfRangeException(nameof(maxLines), "Declaration source exceeds maxLines; increase maxLines.");

        var sourceText = text.ToString(span).TrimEnd();
        if (Encoding.UTF8.GetByteCount(sourceText) > maxBytes)
            throw new ArgumentOutOfRangeException(nameof(maxBytes), "Declaration source exceeds maxBytes; increase maxBytes.");

        return new SymbolSourceBlock
        {
            Path = document.FilePath is null ? "" : ToRelative(document.FilePath),
            StartLine = start.Line + 1,
            StartColumn = start.Character + 1,
            EndLine = end.Line + 1,
            EndColumn = end.Character + 1,
            Text = sourceText,
        };
    }

    private static SyntaxNode? FindDeclarationNode(
        SyntaxNode root,
        SemanticModel model,
        ISymbol symbol,
        TextSpan sourceSpan,
        CancellationToken cancellationToken)
    {
        var node = root.FindNode(sourceSpan, getInnermostNodeForTie: true);
        foreach (var candidate in node.AncestorsAndSelf())
        {
            var declared = model.GetDeclaredSymbol(candidate, cancellationToken);
            if (declared is not null &&
                SymbolEqualityComparer.Default.Equals(declared.OriginalDefinition, symbol.OriginalDefinition))
            {
                return NormalizeDeclarationNode(candidate);
            }
        }

        return node.AncestorsAndSelf()
            .FirstOrDefault(candidate => candidate is MemberDeclarationSyntax or AccessorDeclarationSyntax);
    }

    private static SyntaxNode NormalizeDeclarationNode(SyntaxNode node) =>
        node switch
        {
            VariableDeclaratorSyntax variable when variable.Parent?.Parent is FieldDeclarationSyntax field => field,
            VariableDeclaratorSyntax variable when variable.Parent?.Parent is EventFieldDeclarationSyntax eventField => eventField,
            AccessorDeclarationSyntax accessor when accessor.Parent?.Parent is BasePropertyDeclarationSyntax property => property,
            ParameterSyntax parameter when parameter.Parent?.Parent is BaseMethodDeclarationSyntax method => method,
            ParameterSyntax parameter when parameter.Parent?.Parent is LocalFunctionStatementSyntax localFunction => localFunction,
            ParameterSyntax parameter when parameter.Parent?.Parent is DelegateDeclarationSyntax delegateDeclaration => delegateDeclaration,
            TypeParameterSyntax typeParameter when typeParameter.Parent?.Parent is TypeDeclarationSyntax typeDeclaration => typeDeclaration,
            TypeParameterSyntax typeParameter when typeParameter.Parent?.Parent is MethodDeclarationSyntax method => method,
            TypeParameterSyntax typeParameter when typeParameter.Parent?.Parent is LocalFunctionStatementSyntax localFunction => localFunction,
            TypeParameterSyntax typeParameter when typeParameter.Parent?.Parent is DelegateDeclarationSyntax delegateDeclaration => delegateDeclaration,
            _ => node,
        };

    private static TextSpan TrimLeadingBlankLines(SourceText text, TextSpan span)
    {
        var start = span.Start;
        while (start < span.End)
        {
            var line = text.Lines.GetLineFromPosition(start);
            if (!string.IsNullOrWhiteSpace(line.ToString()))
                break;

            var next = line.EndIncludingLineBreak;
            if (next <= start)
                break;
            start = next;
        }

        return TextSpan.FromBounds(start, span.End);
    }

    private static IEnumerable<string> CandidateSymbolIds(string symbolId)
    {
        yield return symbolId;

        var repaired = RepairStrippedGenericTypeArity(symbolId);
        if (!string.Equals(repaired, symbolId, StringComparison.Ordinal))
            yield return repaired;
    }

    private static string RepairStrippedGenericTypeArity(string symbolId)
    {
        if (!symbolId.StartsWith("T:", StringComparison.Ordinal) || symbolId.Contains('`', StringComparison.Ordinal))
            return symbolId;

        return Regex.Replace(
            symbolId,
            @"(?<prefix>(?:^|[.+])[^.+`\d][^.+`]*?)(?<arity>[1-9]\d*)$",
            match => $"{match.Groups["prefix"].Value}`{match.Groups["arity"].Value}",
            RegexOptions.CultureInvariant);
    }

    private static ISymbol? ResolveStrippedGenericTypeSymbol(
        IReadOnlyList<Compilation> compilations,
        string symbolId)
    {
        if (!symbolId.StartsWith("T:", StringComparison.Ordinal) || symbolId.Contains('`', StringComparison.Ordinal))
            return null;

        var metadataName = symbolId[2..];
        var simpleName = metadataName.Split('.').Last();
        var matches = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);

        foreach (var compilation in compilations)
        {
            foreach (var symbol in EnumerateNamedTypes(compilation.GlobalNamespace))
            {
                if (symbol.TypeParameters.Length == 0)
                    continue;

                var id = DocumentationCommentId.CreateDeclarationId(symbol.OriginalDefinition);
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                var stripped = StripGenericArity(id);
                var strippedSimpleName = symbol.Name;
                if (!string.Equals(stripped, symbolId, StringComparison.Ordinal) &&
                    !string.Equals(stripped[2..], metadataName, StringComparison.Ordinal) &&
                    !string.Equals(strippedSimpleName, simpleName, StringComparison.Ordinal))
                {
                    continue;
                }

                matches.TryAdd(id, symbol.OriginalDefinition);
            }
        }

        return matches.Count == 1 ? matches.Values.Single() : null;
    }

    private static string StripGenericArity(string documentationCommentId) =>
        Regex.Replace(documentationCommentId, @"`\d+", "", RegexOptions.CultureInvariant);

    private static IEnumerable<INamedTypeSymbol> EnumerateNamedTypes(INamespaceOrTypeSymbol container)
    {
        foreach (var member in container.GetMembers())
        {
            if (member is INamedTypeSymbol named)
                yield return named;

            if (member is INamespaceOrTypeSymbol nested)
            {
                foreach (var child in EnumerateNamedTypes(nested))
                    yield return child;
            }
        }
    }

    private SymbolSummary? ToSummary(Solution solution, ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(location => location.IsInSource);
        return location is null ? null : ToSummary(solution, symbol, location);
    }

    private SymbolSummary? ToSummary(Solution solution, ISymbol symbol, Location location)
    {
        if (!location.IsInSource || location.SourceTree is null)
            return null;

        var document = solution.GetDocument(location.SourceTree);
        if (document?.FilePath is null)
            return null;

        var span = location.GetLineSpan().StartLinePosition;
        return new SymbolSummary
        {
            Name = symbol.Name,
            Kind = ToKind(symbol),
            SymbolId = DocumentationCommentId.CreateDeclarationId(symbol) ?? "",
            ProjectName = document.Project.Name,
            Path = ToRelative(document.FilePath),
            Line = span.Line + 1,
            Signature = SymbolSignature(symbol),
            Namespace = SymbolNamespace(symbol),
            ContainingType = SymbolContainingType(symbol),
            Accessibility = symbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
        };
    }

    private ContextMessenger.Core.Roslyn.ReferenceLocation? ToReferenceLocation(
        Solution solution,
        string symbolId,
        Location location,
        bool isDefinition,
        string kind)
    {
        if (location.SourceTree is null)
            return null;

        var document = solution.GetDocument(location.SourceTree);
        if (document?.FilePath is null)
            return null;

        var text = location.SourceTree.GetText();
        var span = location.GetLineSpan().StartLinePosition;
        var lineText = span.Line >= 0 && span.Line < text.Lines.Count
            ? text.Lines[span.Line].ToString()
            : "";

        return new ContextMessenger.Core.Roslyn.ReferenceLocation
        {
            SymbolId = symbolId,
            ProjectName = document.Project.Name,
            Path = ToRelative(document.FilePath),
            Line = span.Line + 1,
            Column = span.Character + 1,
            LineText = lineText.TrimEnd(),
            IsDefinition = isDefinition,
            Kind = kind,
        };
    }

    private static string ToKind(ISymbol symbol) =>
        symbol switch
        {
            INamedTypeSymbol { TypeKind: TypeKind.Class } => "class",
            INamedTypeSymbol { TypeKind: TypeKind.Interface } => "interface",
            INamedTypeSymbol { TypeKind: TypeKind.Struct } => "struct",
            INamedTypeSymbol { TypeKind: TypeKind.Enum } => "enum",
            INamedTypeSymbol { TypeKind: TypeKind.Delegate } => "delegate",
            IMethodSymbol { MethodKind: MethodKind.Constructor } => "constructor",
            IMethodSymbol => "method",
            IPropertySymbol => "property",
            IFieldSymbol => "field",
            IEventSymbol => "event",
            INamespaceSymbol => "namespace",
            _ => symbol.Kind.ToString().ToLowerInvariant(),
        };

    private static string? SymbolSignature(ISymbol symbol)
    {
        var format = new SymbolDisplayFormat(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            kindOptions: SymbolDisplayKindOptions.IncludeTypeKeyword,
            localOptions: SymbolDisplayLocalOptions.IncludeType,
            memberOptions:
                SymbolDisplayMemberOptions.IncludeParameters |
                SymbolDisplayMemberOptions.IncludeType,
            parameterOptions:
                SymbolDisplayParameterOptions.IncludeType |
                SymbolDisplayParameterOptions.IncludeName,
            propertyStyle: SymbolDisplayPropertyStyle.ShowReadWriteDescriptor,
            miscellaneousOptions:
                SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
                SymbolDisplayMiscellaneousOptions.UseSpecialTypes);
        var text = $"{AccessibilityPrefix(symbol)}{symbol.ToDisplayString(format)}";
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string AccessibilityPrefix(ISymbol symbol)
    {
        var accessibility = symbol.DeclaredAccessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Private => "private",
            Accessibility.Internal => "internal",
            Accessibility.Protected => "protected",
            Accessibility.ProtectedAndInternal => "private protected",
            Accessibility.ProtectedOrInternal => "protected internal",
            _ => "",
        };
        var modifiers = new List<string>();
        if (!string.IsNullOrEmpty(accessibility))
            modifiers.Add(accessibility);
        if (symbol.IsStatic)
            modifiers.Add("static");
        if (symbol.IsAbstract && symbol is not INamedTypeSymbol { TypeKind: TypeKind.Interface })
            modifiers.Add("abstract");
        if (symbol.IsSealed && symbol is INamedTypeSymbol { TypeKind: TypeKind.Class })
            modifiers.Add("sealed");
        if (symbol.IsVirtual)
            modifiers.Add("virtual");
        if (symbol.IsOverride)
            modifiers.Add("override");

        return modifiers.Count == 0 ? "" : string.Join(" ", modifiers) + " ";
    }

    private static string? SymbolNamespace(ISymbol symbol)
    {
        var ns = symbol.ContainingNamespace;
        if (ns is null || ns.IsGlobalNamespace)
            return null;

        return ns.ToDisplayString();
    }

    private static string? SymbolContainingType(ISymbol symbol)
    {
        var containingType = symbol.ContainingType;
        if (containingType is null)
            return null;

        return containingType.ToDisplayString(new SymbolDisplayFormat(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters));
    }

    private static IReadOnlyList<string> SymbolAttributes(ISymbol symbol) =>
        symbol.GetAttributes()
            .Select(attribute => attribute.ToString())
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!)
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> SymbolBaseTypes(ISymbol symbol)
    {
        if (symbol is not INamedTypeSymbol named)
            return [];

        var result = new List<string>();
        for (var current = named.BaseType; current is not null; current = current.BaseType)
        {
            if (current.SpecialType == SpecialType.System_Object)
                break;

            result.Add(SymbolFullName(current));
        }

        return result;
    }

    private static IReadOnlyList<string> SymbolImplementedInterfaces(ISymbol symbol) =>
        symbol is INamedTypeSymbol named
            ? named.AllInterfaces.Select(SymbolFullName).OrderBy(text => text, StringComparer.Ordinal).ToArray()
            : [];

    private static IReadOnlyList<string> SymbolTypeParameters(ISymbol symbol) =>
        symbol switch
        {
            INamedTypeSymbol named => named.TypeParameters.Select(parameter => parameter.Name).ToArray(),
            IMethodSymbol method => method.TypeParameters.Select(parameter => parameter.Name).ToArray(),
            _ => [],
        };

    private static IReadOnlyList<string> SymbolGenericConstraints(ISymbol symbol)
    {
        var typeParameters = symbol switch
        {
            INamedTypeSymbol named => named.TypeParameters,
            IMethodSymbol method => method.TypeParameters,
            _ => [],
        };

        return typeParameters
            .Select(FormatConstraint)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!)
            .ToArray();
    }

    private static string? SymbolReturnType(ISymbol symbol) =>
        symbol is IMethodSymbol method ? SymbolFullName(method.ReturnType) : null;

    private static IReadOnlyList<SymbolParameterInfo> SymbolParameters(ISymbol symbol) =>
        symbol is IMethodSymbol method
            ? method.Parameters.Select(parameter => new SymbolParameterInfo
            {
                Name = parameter.Name,
                Type = SymbolFullName(parameter.Type),
                RefKind = parameter.RefKind == RefKind.None ? null : parameter.RefKind.ToString().ToLowerInvariant(),
                IsOptional = parameter.IsOptional,
                DefaultValue = parameter.HasExplicitDefaultValue ? parameter.ExplicitDefaultValue?.ToString() ?? "null" : null,
            }).ToArray()
            : [];

    private static string? SymbolOverriddenMethod(ISymbol symbol) =>
        symbol is IMethodSymbol { OverriddenMethod: { } overridden }
            ? DocumentationCommentId.CreateDeclarationId(overridden.OriginalDefinition)
            : null;

    private static IReadOnlyList<string> SymbolImplementedInterfaceMembers(ISymbol symbol)
    {
        if (symbol is not IMethodSymbol method)
            return [];

        var containingType = method.ContainingType;
        if (containingType is null)
            return [];

        return containingType.AllInterfaces
            .SelectMany(iface => iface.GetMembers().OfType<IMethodSymbol>())
            .Where(member =>
            {
                var implementation = containingType.FindImplementationForInterfaceMember(member);
                return implementation is not null &&
                       SymbolEqualityComparer.Default.Equals(implementation.OriginalDefinition, method.OriginalDefinition);
            })
            .Select(member => DocumentationCommentId.CreateDeclarationId(member.OriginalDefinition))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }

    private static string? FormatConstraint(ITypeParameterSymbol parameter)
    {
        var constraints = new List<string>();
        if (parameter.HasReferenceTypeConstraint)
            constraints.Add(parameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
        if (parameter.HasUnmanagedTypeConstraint)
            constraints.Add("unmanaged");
        else if (parameter.HasValueTypeConstraint)
            constraints.Add("struct");
        if (parameter.HasNotNullConstraint)
            constraints.Add("notnull");
        constraints.AddRange(parameter.ConstraintTypes.Select(SymbolFullName));
        if (parameter.HasConstructorConstraint)
            constraints.Add("new()");

        return constraints.Count == 0
            ? null
            : $"{parameter.Name} : {string.Join(", ", constraints)}";
    }

    private static string SymbolFullName(ISymbol symbol) =>
        symbol.ToDisplayString(new SymbolDisplayFormat(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions:
                SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
                SymbolDisplayMiscellaneousOptions.UseSpecialTypes));

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static string SummaryIdentity(SymbolSummary summary) =>
        string.IsNullOrWhiteSpace(summary.SymbolId)
            ? $"{summary.ProjectName}|{summary.Path}|{summary.Line}|{summary.Kind}|{summary.Name}"
            : summary.SymbolId;

    private static IReadOnlyList<SymbolSummary> SortSummaries(IEnumerable<SymbolSummary> summaries) =>
        summaries
            .OrderBy(summary => summary.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(summary => summary.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(summary => summary.Line)
            .ThenBy(summary => summary.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private string ResolveAbsolute(string pathUnderRoot)
    {
        var input = string.IsNullOrEmpty(pathUnderRoot) ? "." : pathUnderRoot;
        var combined = Path.IsPathRooted(input) ? input : Path.Combine(_root, input);
        var full = TrimTrailingSeparator(Path.GetFullPath(combined));

        if (!IsInsideRoot(full))
            throw new PathOutsideSandboxException(pathUnderRoot);

        return full;
    }

    private bool IsInsideRoot(string absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath)) return false;
        var full = TrimTrailingSeparator(Path.GetFullPath(absolutePath));

        if (string.Equals(full, _root, StringComparison.OrdinalIgnoreCase))
            return true;

        var rootWithSep = _root + Path.DirectorySeparatorChar;
        return full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase);
    }

    private string ToRelative(string absolutePath)
    {
        var full = ResolveAbsolute(absolutePath);
        if (string.Equals(full, _root, StringComparison.OrdinalIgnoreCase))
            return ".";

        var rel = Path.GetRelativePath(_root, full);
        return rel.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string TrimTrailingSeparator(string path)
    {
        if (path.Length < 2) return path;
        var last = path[^1];
        if ((last == '/' || last == '\\') && path[^2] != ':')
            return path[..^1];
        return path;
    }
}
