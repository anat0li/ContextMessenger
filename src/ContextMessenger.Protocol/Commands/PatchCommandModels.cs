using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ContextMessenger.Core.Patching;
using ContextMessenger.Protocol.Compression;

namespace ContextMessenger.Protocol.Commands;

public sealed class PatchPolicyParams
{
    [JsonPropertyName("policy")]
    public string Policy { get; set; } = "none";

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("projects")]
    public IReadOnlyList<string> Projects { get; set; } = [];

    [JsonPropertyName("filter")]
    public string? Filter { get; set; }

    [JsonPropertyName("configuration")]
    public string Configuration { get; set; } = "Debug";

    [JsonPropertyName("timeoutSeconds")]
    public int TimeoutSeconds { get; set; } = 120;

    [JsonPropertyName("treatWarningsAsErrors")]
    public bool TreatWarningsAsErrors { get; set; }

    public PatchPolicy ToCore() => new()
    {
        Policy = string.IsNullOrWhiteSpace(Policy) ? "none" : Policy,
        Path = Path,
        Projects = Projects,
        Filter = Filter,
        Configuration = string.IsNullOrWhiteSpace(Configuration) ? "Debug" : Configuration,
        TimeoutSeconds = TimeoutSeconds <= 0 ? 120 : TimeoutSeconds,
        TreatWarningsAsErrors = TreatWarningsAsErrors,
    };
}

public sealed class PatchFileOperationParams
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("operation")]
    public string Operation { get; set; } = "";

    [JsonPropertyName("oldContentHash")]
    public string? OldContentHash { get; set; }

    [JsonPropertyName("newContent")]
    public string? NewContent { get; set; }

    [JsonPropertyName("newContentEncoding")]
    public string? NewContentEncoding { get; set; }

    public PatchFileOperation ToCore()
    {
        var kind = Operation.ToLowerInvariant() switch
        {
            "create" => PatchFileOperationKind.Create,
            "replace" => PatchFileOperationKind.Replace,
            "delete" => PatchFileOperationKind.Delete,
            _ => throw new ProtocolException(
                ProtocolErrorCodes.InvalidParameters,
                $"Unsupported patch file operation '{Operation}'. Use create, replace, or delete."),
        };

        return new PatchFileOperation
        {
            Path = Path,
            Operation = kind,
            OldContentHash = OldContentHash,
            NewContent = DecodeNewContent(),
        };
    }

    private string? DecodeNewContent()
    {
        if (string.IsNullOrWhiteSpace(NewContentEncoding))
            return NewContent;

        if (string.Equals(NewContentEncoding, "gzipbase64utf8", StringComparison.OrdinalIgnoreCase))
        {
            if (NewContent is null)
            {
                throw new ProtocolException(
                    ProtocolErrorCodes.InvalidParameters,
                    "newContent is required when newContentEncoding is gzipbase64utf8.");
            }

            return GzipBase64.Decode(NewContent, "newContent");
        }

        if (!string.Equals(NewContentEncoding, "base64utf8", StringComparison.OrdinalIgnoreCase))
        {
            throw new ProtocolException(
                ProtocolErrorCodes.InvalidParameters,
                $"Unsupported newContentEncoding '{NewContentEncoding}'. Use base64utf8, gzipbase64utf8, or omit the field.");
        }

        if (NewContent is null)
        {
            throw new ProtocolException(
                ProtocolErrorCodes.InvalidParameters,
                "newContent is required when newContentEncoding is base64utf8.");
        }

        try
        {
            var bytes = Convert.FromBase64String(NewContent);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch (FormatException ex)
        {
            throw new ProtocolException(
                ProtocolErrorCodes.InvalidParameters,
                $"newContent is not valid base64utf8 content: {ex.Message}");
        }
    }
}

public sealed class PatchEditOperationParams
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("oldText")]
    public string? OldText { get; set; }

    [JsonPropertyName("oldTextEncoding")]
    public string? OldTextEncoding { get; set; }

    [JsonPropertyName("newText")]
    public string? NewText { get; set; }

    [JsonPropertyName("newTextEncoding")]
    public string? NewTextEncoding { get; set; }

    [JsonPropertyName("anchor")]
    public string? Anchor { get; set; }

    [JsonPropertyName("anchorEncoding")]
    public string? AnchorEncoding { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("textEncoding")]
    public string? TextEncoding { get; set; }

    [JsonPropertyName("expectedFileHash")]
    public string? ExpectedFileHash { get; set; }

    [JsonPropertyName("expectedAnchorHash")]
    public string? ExpectedAnchorHash { get; set; }

    [JsonPropertyName("startLine")]
    public int? StartLine { get; set; }

    [JsonPropertyName("endLine")]
    public int? EndLine { get; set; }

    [JsonPropertyName("oldRangeHash")]
    public string? OldRangeHash { get; set; }

    [JsonPropertyName("pointer")]
    public string? Pointer { get; set; }

    [JsonPropertyName("value")]
    public JsonElement? Value { get; set; }

    [JsonPropertyName("symbolId")]
    public string? SymbolId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("match")]
    public string Match { get; set; } = "exact";

    [JsonPropertyName("kinds")]
    public IReadOnlyList<string> Kinds { get; set; } = [];

    [JsonPropertyName("project")]
    public string? Project { get; set; }

    [JsonPropertyName("includeNonPublic")]
    public bool IncludeNonPublic { get; set; } = true;

    [JsonPropertyName("line")]
    public int? Line { get; set; }

    [JsonPropertyName("column")]
    public int? Column { get; set; }

    [JsonPropertyName("oldSourceHash")]
    public string? OldSourceHash { get; set; }

    public PatchEditOperation ToCore() => new()
    {
        Path = Path,
        Kind = Kind,
        OldText = DecodeTextField(OldText, OldTextEncoding, "oldText"),
        NewText = DecodeTextField(NewText, NewTextEncoding, "newText"),
        Anchor = DecodeTextField(Anchor, AnchorEncoding, "anchor"),
        Text = DecodeTextField(Text, TextEncoding, "text"),
        ExpectedFileHash = ExpectedFileHash,
        ExpectedAnchorHash = ExpectedAnchorHash,
        StartLine = StartLine,
        EndLine = EndLine,
        OldRangeHash = OldRangeHash,
        Pointer = Pointer,
        ValueSpecified = Value.HasValue,
        Value = Value.HasValue ? JsonNode.Parse(Value.Value.GetRawText()) : null,
        SymbolId = SymbolId,
        Name = Name,
        Match = string.IsNullOrWhiteSpace(Match) ? "exact" : Match,
        Kinds = Kinds,
        Project = Project,
        IncludeNonPublic = IncludeNonPublic,
        Line = Line,
        Column = Column,
        OldSourceHash = OldSourceHash,
    };

    private static string? DecodeTextField(string? value, string? encoding, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(encoding))
            return value;

        if (!string.Equals(encoding, "base64utf8", StringComparison.OrdinalIgnoreCase))
        {
            throw new ProtocolException(
                ProtocolErrorCodes.InvalidParameters,
                $"Unsupported {fieldName}Encoding '{encoding}'. Use base64utf8 or omit the field.");
        }

        if (value is null)
        {
            throw new ProtocolException(
                ProtocolErrorCodes.InvalidParameters,
                $"{fieldName} is required when {fieldName}Encoding is base64utf8.");
        }

        try
        {
            var bytes = Convert.FromBase64String(value);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch (FormatException ex)
        {
            throw new ProtocolException(
                ProtocolErrorCodes.InvalidParameters,
                $"{fieldName} is not valid base64utf8 content: {ex.Message}");
        }
    }
}

public sealed class PatchTransactionCommandResult
{
    [JsonPropertyName("patchStatus")]
    public string PatchStatus { get; set; } = "";

    [JsonPropertyName("patchId")]
    public string? PatchId { get; set; }

    [JsonPropertyName("revision")]
    public int Revision { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("commitMessage")]
    public string? CommitMessage { get; set; }

    [JsonPropertyName("recovered")]
    public bool Recovered { get; set; }

    [JsonPropertyName("lastFailureStage")]
    public string? LastFailureStage { get; set; }

    [JsonPropertyName("applied")]
    public bool Applied { get; set; }

    [JsonPropertyName("diffVerified")]
    public bool DiffVerified { get; set; }

    [JsonPropertyName("build")]
    public PatchStageCommandResult? Build { get; set; }

    [JsonPropertyName("tests")]
    public PatchStageCommandResult? Tests { get; set; }

    [JsonPropertyName("warnings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<PatchWarningCommandResult>? Warnings { get; set; }

    [JsonPropertyName("files")]
    public IReadOnlyList<PatchFileStateCommandResult> Files { get; set; } = [];

    public static PatchTransactionCommandResult FromCore(PatchTransactionResult result) => new()
    {
        PatchStatus = result.PatchStatus,
        PatchId = result.PatchId,
        Revision = result.Revision,
        Title = result.Title,
        Description = result.Description,
        CommitMessage = result.CommitMessage,
        Recovered = result.Recovered,
        LastFailureStage = result.LastFailureStage,
        Applied = result.Applied,
        DiffVerified = result.DiffVerified,
        Build = result.Build is null ? null : PatchStageCommandResult.FromCore(result.Build),
        Tests = result.Tests is null ? null : PatchStageCommandResult.FromCore(result.Tests),
        Warnings = result.Warnings.Count == 0 ? null : result.Warnings.Select(PatchWarningCommandResult.FromCore).ToArray(),
        Files = result.Files.Select(PatchFileStateCommandResult.FromCore).ToArray(),
    };
}

public sealed class ValidatePatchCommandResult
{
    [JsonPropertyName("valid")]
    public bool Valid { get; set; }

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "";

    [JsonPropertyName("patchId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PatchId { get; set; }

    [JsonPropertyName("baseRevision")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? BaseRevision { get; set; }

    [JsonPropertyName("applied")]
    public bool Applied { get; set; }

    [JsonPropertyName("diffVerified")]
    public bool DiffVerified { get; set; }

    [JsonPropertyName("build")]
    public PatchStageCommandResult Build { get; set; } = new();

    [JsonPropertyName("tests")]
    public PatchStageCommandResult Tests { get; set; } = new();

    [JsonPropertyName("warnings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<PatchWarningCommandResult>? Warnings { get; set; }

    [JsonPropertyName("files")]
    public IReadOnlyList<PatchFileStateCommandResult> Files { get; set; } = [];

    public static ValidatePatchCommandResult FromCore(PatchValidationResult result) => new()
    {
        Valid = result.Valid,
        Mode = result.Mode,
        PatchId = result.PatchId,
        BaseRevision = result.BaseRevision,
        Applied = result.Applied,
        DiffVerified = result.DiffVerified,
        Build = PatchStageCommandResult.FromCore(result.Build),
        Tests = PatchStageCommandResult.FromCore(result.Tests),
        Warnings = result.Warnings.Count == 0 ? null : result.Warnings.Select(PatchWarningCommandResult.FromCore).ToArray(),
        Files = result.Files.Select(PatchFileStateCommandResult.FromCore).ToArray(),
    };
}

public sealed class PatchWarningCommandResult
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; set; }

    [JsonPropertyName("editIndex")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? EditIndex { get; set; }

    [JsonPropertyName("kind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Kind { get; set; }

    public static PatchWarningCommandResult FromCore(PatchWarning warning) => new()
    {
        Code = warning.Code,
        Message = warning.Message,
        Path = warning.Path,
        EditIndex = warning.EditIndex,
        Kind = warning.Kind,
    };
}

public sealed class PatchStageCommandResult
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("policy")]
    public string? Policy { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("projects")]
    public IReadOnlyList<string> Projects { get; set; } = [];

    [JsonPropertyName("filter")]
    public string? Filter { get; set; }

    [JsonPropertyName("configuration")]
    public string? Configuration { get; set; }

    [JsonPropertyName("durationMs")]
    public int? DurationMs { get; set; }

    [JsonPropertyName("exitCode")]
    public int? ExitCode { get; set; }

    [JsonPropertyName("totalTests")]
    public int? TotalTests { get; set; }

    [JsonPropertyName("executedTests")]
    public int? ExecutedTests { get; set; }

    [JsonPropertyName("passedTests")]
    public int? PassedTests { get; set; }

    [JsonPropertyName("failedTests")]
    public int? FailedTests { get; set; }

    [JsonPropertyName("skippedTests")]
    public int? SkippedTests { get; set; }

    [JsonPropertyName("stdout")]
    public string? Stdout { get; set; }

    [JsonPropertyName("stdoutTruncated")]
    public bool StdoutTruncated { get; set; }

    [JsonPropertyName("stderr")]
    public string? Stderr { get; set; }

    [JsonPropertyName("stderrTruncated")]
    public bool StderrTruncated { get; set; }

    [JsonPropertyName("diagnostics")]
    public IReadOnlyList<BuildDiagnosticCommandResult> Diagnostics { get; set; } = [];

    public static PatchStageCommandResult FromCore(PatchStageResult result) => new()
    {
        Status = result.Status,
        Policy = result.Policy,
        Path = result.Path,
        Projects = result.Projects,
        Filter = result.Filter,
        Configuration = result.Configuration,
        DurationMs = result.DurationMs,
        ExitCode = result.ExitCode,
        TotalTests = result.TotalTests,
        ExecutedTests = result.ExecutedTests,
        PassedTests = result.PassedTests,
        FailedTests = result.FailedTests,
        SkippedTests = result.SkippedTests,
        Stdout = result.Stdout,
        StdoutTruncated = result.StdoutTruncated,
        Stderr = result.Stderr,
        StderrTruncated = result.StderrTruncated,
        Diagnostics = result.Diagnostics.Select(BuildDiagnosticCommandResult.FromCore).ToArray(),
    };
}

public sealed class BuildDiagnosticCommandResult
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("line")]
    public int? Line { get; set; }

    [JsonPropertyName("column")]
    public int? Column { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    public static BuildDiagnosticCommandResult FromCore(BuildDiagnostic diagnostic) => new()
    {
        Kind = diagnostic.Kind,
        Code = diagnostic.Code,
        Path = diagnostic.Path,
        Line = diagnostic.Line,
        Column = diagnostic.Column,
        Message = diagnostic.Message,
    };
}

public sealed class PatchFileStateCommandResult
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("operation")]
    public string Operation { get; set; } = "";

    [JsonPropertyName("oldContentHash")]
    public string? OldContentHash { get; set; }

    [JsonPropertyName("currentContentHash")]
    public string? CurrentContentHash { get; set; }

    [JsonPropertyName("lastRevision")]
    public int LastRevision { get; set; }

    public static PatchFileStateCommandResult FromCore(PatchFileState state) => new()
    {
        Path = state.Path,
        Operation = state.Operation,
        OldContentHash = state.OldContentHash,
        CurrentContentHash = state.CurrentContentHash,
        LastRevision = state.LastRevision,
    };
}
