using ContextMessenger.Core.FileSystem;
using ContextMessenger.Core.Meta;
using ContextMessenger.Core.Patching;
using ContextMessenger.Core.Roslyn;
using ContextMessenger.Data;
using ContextMessenger.Protocol.Commands;
using ContextMessenger.Protocol.Wire;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ContextMessenger.Protocol.Dispatch;

public sealed class CommandDispatcher
{
    private readonly Dictionary<string, ICommandHandler> _handlers;
    private readonly IRequestIdCache _idCache;

    public CommandDispatcher(IEnumerable<ICommandHandler> handlers, IRequestIdCache? idCache = null)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        _handlers = handlers.ToDictionary(h => h.CommandType, StringComparer.Ordinal);
        _idCache = idCache ?? new InMemoryRequestIdCache();
    }

    public static CommandDispatcher ForFileSystem(IFileSystemService fs, IRequestIdCache? idCache = null)
        => ForServices(fs, roslyn: null, session: null, idCache);

    public static CommandDispatcher ForServices(
        IFileSystemService? fs,
        IRoslynNavigationService? roslyn,
        IContextSession? session,
        IGitStatusService? gitStatus,
        IPatchTransactionService? patchTransactions,
        IRequestIdCache? idCache = null,
        IDataRootSession? dataRootSession = null,
        bool allowSchemaCommands = true)
    {
        var handlers = new List<ICommandHandler>();

        if (fs is not null)
        {
            handlers.Add(new TreeHandler(fs));
            handlers.Add(new ReadFileHandler(fs));
            handlers.Add(new SearchTextHandler(fs));
            handlers.Add(new ListFilesHandler(fs));
            handlers.Add(new ProjectInfoHandler(fs));
        }

        if (gitStatus is not null)
            handlers.Add(new GitStatusHandler(gitStatus));

        if (patchTransactions is not null)
        {
            handlers.Add(new ProposePatchHandler(patchTransactions));
            handlers.Add(new AmendPatchHandler(patchTransactions));
            handlers.Add(new ValidatePatchHandler(patchTransactions));
            handlers.Add(new CurrentPatchHandler(patchTransactions));
            handlers.Add(new RevertPatchHandler(patchTransactions));
        }

        if (roslyn is not null)
        {
            handlers.Add(new DocumentSymbolsHandler(roslyn));
            handlers.Add(new FindSymbolHandler(roslyn));
            handlers.Add(new FindReferencesHandler(roslyn));
            handlers.Add(new GotoDefinitionHandler(roslyn));
            handlers.Add(new FindImplementationsHandler(roslyn));
            handlers.Add(new FindCallersHandler(roslyn));
            handlers.Add(new FindDerivedTypesHandler(roslyn));
            handlers.Add(new FindOverridesHandler(roslyn));
            handlers.Add(new GetSymbolInfoHandler(roslyn));
            handlers.Add(new GetSymbolSourceHandler(roslyn));
        }

        if (session is not null)
        {
            handlers.Add(new CurrentContextHandler(session));
            handlers.Add(new ListRootsHandler(session));
            handlers.Add(new SetRootHandler(session, patchTransactions));
            handlers.Add(new ListTargetsHandler(session));
        }

        if (dataRootSession is not null)
        {
            if (allowSchemaCommands)
            {
                handlers.Add(new SqlSchemaHandler(dataRootSession));
                handlers.Add(new SqlTablesHandler(dataRootSession));
                handlers.Add(new SqlColumnsHandler(dataRootSession));
            }
            handlers.Add(new SqlQueryHandler(dataRootSession));
        }

        handlers.Add(new CapabilitiesHandler(
            handlers.Select(handler => handler.CommandType).Append(CommandTypes.Capabilities)));

        return new CommandDispatcher(handlers, idCache);
    }

    public static CommandDispatcher ForServices(
        IFileSystemService fs,
        IRoslynNavigationService? roslyn,
        IContextSession? session,
        IGitStatusService? gitStatus,
        IRequestIdCache? idCache = null)
        => ForServices(fs, roslyn, session, gitStatus, patchTransactions: null, idCache);

    public static CommandDispatcher ForServices(
        IFileSystemService fs,
        IRoslynNavigationService? roslyn,
        IContextSession? session,
        IRequestIdCache? idCache = null)
        => ForServices(fs, roslyn, session, gitStatus: null, patchTransactions: null, idCache);

    public static CommandDispatcher ForServices(
        IFileSystemService fs,
        IRoslynNavigationService? roslyn = null,
        IRequestIdCache? idCache = null)
        => ForServices(fs, roslyn, session: null, gitStatus: null, patchTransactions: null, idCache);

    public IReadOnlyCollection<string> RegisteredCommands => _handlers.Keys;

    public ContextResponse Dispatch(ContextRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return DispatchSingle(request, cancellationToken);
    }

    public IReadOnlyList<ContextResponse> Dispatch(
        IReadOnlyList<ContextRequest> requests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);

        var responses = new List<ContextResponse>(requests.Count);
        foreach (var request in requests)
        {
            if (!_idCache.TryAdd(request.Id))
            {
                // Debug.WriteLine('[ContextMessenger] Skipped duplicate request id {request.Id}');
                continue;
            }

            responses.Add(DispatchSingle(request, cancellationToken));
        }

        return responses;
    }

    private static readonly Regex IdRx = new(
        @"""id""\s*:\s*""(?<id>[0-9A-Fa-f]{8}-?[0-9A-Fa-f]{4}-?[0-9A-Fa-f]{4}-?[0-9A-Fa-f]{4}-?[0-9A-Fa-f]{12})""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string ProcessRequests(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default) =>
        ProcessRequestsDetailed(inputs, cancellationToken).ResponseText;

    public ProcessRequestsResult ProcessRequestsDetailed(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default)
    {
        var allResponses = new List<ContextResponse>();
        // Reviewer-comment replies live in the amend_patch request, keyed by (requestId, commandIndex)
        // so they can be matched back to the produced outcome.
        var commentReplies = new Dictionary<(string Id, int Index), IReadOnlyList<PatchCommentReply>>();
        foreach (var input in inputs)
        {
            var match = IdRx.Match(input);
            string? id = match.Success ? match.Groups["id"].Value : null;
            try
            {
                var requests = ProtocolParser.ParseBody(input);
                id = requests.Count == 1 ? requests[0].Id : null;
                ProtocolValidator.Validate(requests);
                CollectCommentReplies(requests, commentReplies);
                allResponses.AddRange(Dispatch(requests, cancellationToken));
            }
            catch (ProtocolException ex)
            {
                allResponses.Add(ErrorResponse(id, ex.Code, ex.Message));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                allResponses.Add(ErrorResponse(id, ProtocolErrorCodes.InternalError, ex.Message));
            }
        }

        return new ProcessRequestsResult
        {
            ResponseText = ProtocolWriter.Write(allResponses),
            PatchOutcomes = ExtractPatchOutcomes(allResponses, commentReplies),
            IsCancellationResponse = ContainsCancellation(allResponses),
        };
    }

    private static bool ContainsCancellation(IEnumerable<ContextResponse> responses) =>
        responses.Any(response =>
            string.Equals(response.Error?.Code, ProtocolErrorCodes.OperationCancelled, StringComparison.Ordinal) ||
            response.Results?.Any(result =>
                string.Equals(result.Error?.Code, ProtocolErrorCodes.OperationCancelled, StringComparison.Ordinal)) == true);

    private static void CollectCommentReplies(
        IReadOnlyList<ContextRequest> requests,
        Dictionary<(string Id, int Index), IReadOnlyList<PatchCommentReply>> sink)
    {
        foreach (var request in requests)
        {
            for (var i = 0; i < request.Commands.Count; i++)
            {
                if (!string.Equals(request.Commands[i].Type, CommandTypes.AmendPatch, StringComparison.Ordinal))
                    continue;

                var replies = PatchCommentReplyExtractor.FromCommand(request.Commands[i]);
                if (replies.Count > 0)
                    sink[(request.Id, i)] = replies;
            }
        }
    }

    private static IReadOnlyList<PatchOutcome> ExtractPatchOutcomes(
        IReadOnlyList<ContextResponse> responses,
        IReadOnlyDictionary<(string Id, int Index), IReadOnlyList<PatchCommentReply>> commentReplies)
    {
        List<PatchOutcome>? outcomes = null;
        foreach (var response in responses)
        {
            if (response.Results is null)
                continue;

            foreach (var result in response.Results)
            {
                if (!IsOutcomeBearingPatchCommand(result.Type))
                    continue;
                // OK patch results carry patchStatus in the payload; error results do not.
                if (!result.Payload.TryGetValue("patchStatus", out var statusElement) ||
                    statusElement.ValueKind != JsonValueKind.String)
                    continue;

                var status = statusElement.GetString();
                if (string.IsNullOrEmpty(status))
                    continue;

                var buildErrors = result.Payload.TryGetValue("build", out var buildElement)
                    ? PatchBuildErrorExtractor.FromBuildElement(buildElement, "build")
                    : [];
                var testFailures = result.Payload.TryGetValue("tests", out var testsElement)
                    ? PatchTestFailureExtractor.FromTestsElement(testsElement)
                    : [];
                var visibleErrors = AddTestStageErrorsWhenNoTestFailures(
                    buildErrors,
                    testFailures,
                    result.Payload.TryGetValue("tests", out testsElement) ? testsElement : default);

                outcomes ??= [];
                outcomes.Add(new PatchOutcome
                {
                    RequestId = response.Id,
                    CommandType = result.Type,
                    PatchStatus = status,
                    PatchId = result.Payload.TryGetValue("patchId", out var idElement) &&
                              idElement.ValueKind == JsonValueKind.String
                        ? idElement.GetString()
                        : null,
                    Revision = result.Payload.TryGetValue("revision", out var revElement) &&
                               revElement.ValueKind == JsonValueKind.Number
                        ? revElement.GetInt32()
                        : 0,
                    BuildErrors = visibleErrors,
                    BuildWarnings = result.Payload.TryGetValue("build", out buildElement)
                        ? PatchBuildErrorExtractor.WarningsFromBuildElement(buildElement)
                        : [],
                    BuildSummary = result.Payload.TryGetValue("build", out buildElement)
                        ? PatchStageSummary.FromStageElement(buildElement)
                        : PatchStageSummary.Empty,
                    TestFailures = testFailures,
                    TestSummary = result.Payload.TryGetValue("tests", out testsElement)
                        ? PatchStageSummary.FromStageElement(testsElement)
                        : PatchStageSummary.Empty,
                    CommentReplies = commentReplies.TryGetValue((response.Id, result.CommandIndex), out var replies)
                        ? replies
                        : [],
                });
            }
        }

        return outcomes ?? [];
    }

    private static IReadOnlyList<PatchBuildError> AddTestStageErrorsWhenNoTestFailures(
        IReadOnlyList<PatchBuildError> buildErrors,
        IReadOnlyList<PatchTestFailure> testFailures,
        JsonElement testsElement)
    {
        if (testsElement.ValueKind != JsonValueKind.Object || testFailures.Count > 0)
            return buildErrors;

        var testErrors = PatchBuildErrorExtractor.FromBuildElement(testsElement, "tests");
        if (testErrors.Count == 0)
            return buildErrors;

        return [.. buildErrors, .. testErrors];
    }

    private static bool IsOutcomeBearingPatchCommand(string type) =>
        type is CommandTypes.ProposePatch or CommandTypes.AmendPatch or CommandTypes.RevertPatch;

    private static ContextResponse ErrorResponse(string? id, string code, string message) =>
        new()
        {
            Version = ProtocolValidator.CurrentVersion,
            Id = string.IsNullOrWhiteSpace(id) ? "unknown" : id,
            Status = ProtocolStatus.Error,
            ServerTimeUtc = ServerClock.NowIso8601Utc(),
            Error = new ContextResponseError { Code = code, Message = message },
        };

    private ContextResponse DispatchSingle(ContextRequest request, CancellationToken cancellationToken)
    {
        var response = new ContextResponse
        {
            Version = ProtocolValidator.CurrentVersion,
            Id = request.Id,
            Status = ProtocolStatus.Ok,
            ServerTimeUtc = ServerClock.NowIso8601Utc(),
            Results = new List<ContextResponseResult>(request.Commands.Count),
        };

        var lastSetRootIndex = FindLastIndexOfType(request.Commands, CommandTypes.SetRoot);

        for (var i = 0; i < request.Commands.Count; i++)
        {
            var cmd = request.Commands[i];
            if (cancellationToken.IsCancellationRequested)
            {
                response.Results.Add(CancelledResult(i, cmd.Type));
                AddCancellationIgnoredResults(response.Results, request.Commands, i + 1);
                break;
            }

            if (lastSetRootIndex >= 0 &&
                i != lastSetRootIndex &&
                string.Equals(cmd.Type, CommandTypes.SetRoot, StringComparison.Ordinal))
            {
                response.Results.Add(IgnoredResult(i, cmd.Type,
                    "Superseded by a later set_root in the same batch."));
                continue;
            }

            var result = ExecuteOne(cmd, i, cancellationToken);
            response.Results.Add(result);
            if (string.Equals(
                    result.Error?.Code,
                    ProtocolErrorCodes.OperationCancelled,
                    StringComparison.Ordinal))
            {
                AddCancellationIgnoredResults(response.Results, request.Commands, i + 1);
                break;
            }
        }

        return response;
    }

    private static int FindLastIndexOfType(IReadOnlyList<ContextCommand> commands, string type)
    {
        for (var i = commands.Count - 1; i >= 0; i--)
        {
            if (string.Equals(commands[i].Type, type, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }

    private static ContextResponseResult IgnoredResult(int index, string type, string reason)
    {
        var result = new ContextResponseResult
        {
            CommandIndex = index,
            Type = type,
            Status = ProtocolStatus.Ignored,
        };
        result.Payload["reason"] = JsonSerializer.SerializeToElement(reason);
        return result;
    }

    private static ContextResponseResult CancelledResult(int index, string type) =>
        ErrorResult(
            index,
            type,
            ProtocolErrorCodes.OperationCancelled,
            "The operation was cancelled by the user.");

    private static void AddCancellationIgnoredResults(
        List<ContextResponseResult> results,
        IReadOnlyList<ContextCommand> commands,
        int startIndex)
    {
        for (var i = startIndex; i < commands.Count; i++)
        {
            results.Add(IgnoredResult(
                i,
                commands[i].Type,
                "Not executed because the preceding operation was cancelled."));
        }
    }

    private ContextResponseResult ExecuteOne(
        ContextCommand cmd,
        int index,
        CancellationToken cancellationToken)
    {
        if (!_handlers.TryGetValue(cmd.Type, out var handler))
            return ErrorResult(index, cmd.Type,
                ProtocolErrorCodes.UnsupportedCommand,
                $"No handler registered for command type '{cmd.Type}'.");

        try
        {
            return handler.Execute(cmd, index, cancellationToken);
        }
        catch (ProtocolException ex)
        {
            return ErrorResult(index, cmd.Type, ex.Code, ex.Message);
        }
        catch (OperationCanceledException)
        {
            return CancelledResult(index, cmd.Type);
        }
        catch (PathOutsideSandboxException ex)
        {
            return ErrorResult(index, cmd.Type, ProtocolErrorCodes.PathOutsideSandbox, ex.Message);
        }
        catch (PatchValidationException ex)
        {
            return ErrorResult(index, cmd.Type, ex);
        }
        catch (FileNotFoundException ex)
        {
            return ErrorResult(index, cmd.Type, ProtocolErrorCodes.FileNotFound, ex.Message);
        }
        catch (NotSupportedException ex)
        {
            return ErrorResult(index, cmd.Type, ProtocolErrorCodes.UnsupportedFileType, ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Roslyn workspace unavailable:", StringComparison.Ordinal))
        {
            return ErrorResult(index, cmd.Type, ProtocolErrorCodes.WorkspaceUnavailable, ex.Message);
        }
        catch (SymbolNotFoundException ex)
        {
            return ErrorResult(index, cmd.Type, ProtocolErrorCodes.SymbolNotFound, ex.Message);
        }
        catch (DirectoryNotFoundException ex)
        {
            return ErrorResult(index, cmd.Type, ProtocolErrorCodes.DirectoryNotFound, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return ErrorResult(index, cmd.Type, ProtocolErrorCodes.InvalidParameters, ex.Message);
        }
        catch (Exception ex)
        {
            return ErrorResult(index, cmd.Type, ProtocolErrorCodes.InternalError, ex.Message);
        }
    }

    private static ContextResponseResult ErrorResult(int index, string type, string code, string message) =>
        new()
        {
            CommandIndex = index,
            Type = type,
            Status = ProtocolStatus.Error,
            Error = new ContextResponseError { Code = code, Message = message },
        };

    private static ContextResponseResult ErrorResult(int index, string type, PatchValidationException ex) =>
        new()
        {
            CommandIndex = index,
            Type = type,
            Status = ProtocolStatus.Error,
            Error = new ContextResponseError
            {
                Code = ex.Code,
                Message = ex.Message,
                Path = ex.Path,
                EditIndex = ex.EditIndex,
                Kind = ex.Kind,
                MatchCount = ex.MatchCount,
                HashField = ex.HashField,
                ExpectedHash = ex.ExpectedHash,
                ActualHash = ex.ActualHash,
                HashTarget = ex.HashTarget,
                ExpectedFormat = ex.ExpectedFormat,
                LineEndingHint = ex.LineEndingHint,
                Matches = ex.Matches?.Select(m => new ContextResponseMatchLocation
                {
                    Line = m.Line,
                    Column = m.Column,
                }).ToArray(),
            },
        };
}
