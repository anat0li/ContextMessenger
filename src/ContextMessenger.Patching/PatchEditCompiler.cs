using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ContextMessenger.Core.Patching;
using ContextMessenger.Core.Roslyn;
using ContextMessenger.FileSystem;

namespace ContextMessenger.Patching;

internal sealed class PatchEditCompiler
{
    private const string ContentHashFormat = "sha256:<64 lowercase hex characters>";
    private const int MaxMatchLocations = 20;
    private static readonly Regex ContentHashPattern = new(
        "^sha256:[0-9a-f]{64}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _rootPath;
    private readonly PathSandbox _sandbox;
    private readonly IRoslynNavigationService? _roslyn;
    private readonly Dictionary<string, BufferState> _buffers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PatchWarning> _warnings = [];

    public PatchEditCompiler(string rootPath, IRoslynNavigationService? roslyn = null)
    {
        _rootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
        _roslyn = roslyn;
        _sandbox = new PathSandbox(_rootPath);
    }

    public IReadOnlyList<PatchWarning> Warnings => _warnings;

    public IReadOnlyList<PatchFileOperation> Compile(
        IReadOnlyList<PatchFileOperation> files,
        IReadOnlyList<PatchEditOperation> edits)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(edits);

        foreach (var file in files)
            ApplyFileOperation(file);

        for (var i = 0; i < edits.Count; i++)
            ApplyEdit(edits[i], i);

        return _buffers.Values
            .Select(ToOperation)
            .OrderBy(op => op.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void ApplyFileOperation(PatchFileOperation op)
    {
        if (string.IsNullOrWhiteSpace(op.Path))
            throw new PatchValidationException("invalid_patch", "Patch operation path is required.");

        var normalized = NormalizePath(op.Path);
        var state = GetBuffer(normalized);

        switch (op.Operation)
        {
            case PatchFileOperationKind.Create:
                if (state.Exists)
                    throw new PatchValidationException("edit_conflict", $"Cannot create '{normalized}' because it already exists.");
                if (op.NewContent is null)
                    throw new PatchValidationException("invalid_patch", $"Create operation for '{normalized}' requires newContent.");
                state.Content = op.NewContent;
                state.Deleted = false;
                break;

            case PatchFileOperationKind.Replace:
                RequireExistingFile(state, normalized);
                RequireMatchingHash(normalized, state.Content!, op.OldContentHash);
                if (op.NewContent is null)
                    throw new PatchValidationException("invalid_patch", $"Replace operation for '{normalized}' requires newContent.");
                state.Content = op.NewContent;
                state.Deleted = false;
                break;

            case PatchFileOperationKind.Delete:
                RequireExistingFile(state, normalized);
                RequireMatchingHash(normalized, state.Content!, op.OldContentHash);
                if (op.NewContent is not null)
                    throw new PatchValidationException("invalid_patch", $"Delete operation for '{normalized}' must not include newContent.");
                state.Content = null;
                state.Deleted = true;
                break;

            default:
                throw new PatchValidationException("invalid_patch", $"Unsupported patch operation '{op.Operation}'.");
        }
    }

    private void ApplyEdit(PatchEditOperation edit, int editIndex)
    {
        if (string.IsNullOrWhiteSpace(edit.Kind))
            throw new PatchValidationException("invalid_parameters", $"edits[{editIndex}].kind is required.");
        if (string.Equals(edit.Kind, "replace_symbol_source", StringComparison.Ordinal))
        {
            ApplyReplaceSymbolSource(edit, editIndex);
            return;
        }

        if (string.IsNullOrWhiteSpace(edit.Path))
            throw new PatchValidationException("invalid_parameters", $"edits[{editIndex}].path is required.");

        var normalized = NormalizePath(edit.Path);
        var state = GetBuffer(normalized);
        RequireExistingFile(state, normalized, editIndex, edit.Kind);
        CheckExpectedFileHash(edit, editIndex, normalized, state.Content!);

        state.Content = edit.Kind switch
        {
            "replace_exact" => ReplaceExact(state.Content!, Required(edit.OldText, editIndex, edit.Kind, "oldText", normalized), Required(edit.NewText, editIndex, edit.Kind, "newText", normalized), edit, normalized, editIndex, edit.Kind),
            "insert_before_exact" => InsertExact(state.Content!, Required(edit.Anchor, editIndex, edit.Kind, "anchor", normalized), Required(edit.Text, editIndex, edit.Kind, "text", normalized), before: true, edit, normalized, editIndex, edit.Kind),
            "insert_after_exact" => InsertExact(state.Content!, Required(edit.Anchor, editIndex, edit.Kind, "anchor", normalized), Required(edit.Text, editIndex, edit.Kind, "text", normalized), before: false, edit, normalized, editIndex, edit.Kind),
            "delete_exact" => ReplaceExact(state.Content!, Required(edit.OldText, editIndex, edit.Kind, "oldText", normalized), "", edit, normalized, editIndex, edit.Kind),
            "replace_lines" => ReplaceLines(state.Content!, edit, editIndex, normalized),
            "json_set" => JsonSet(state.Content!, edit, editIndex, normalized),
            _ => throw new PatchValidationException(
                    "unsupported_edit_kind",
                    $"Unsupported edit kind '{edit.Kind}' for '{normalized}' at edits[{editIndex}].",
                    path: normalized,
                    editIndex: editIndex,
                    kind: edit.Kind),
        };
    }

    private static string ReplaceExact(string content, string oldText, string newText, PatchEditOperation edit, string path, int editIndex, string kind)
    {
        var match = FindSingleMatch(content, oldText, path, editIndex, kind);
        CheckExpectedAnchorHash(edit, editIndex, path, kind, oldText);
        return content[..match.Index] + newText + content[(match.Index + match.Length)..];
    }

    private static string InsertExact(string content, string anchor, string text, bool before, PatchEditOperation edit, string path, int editIndex, string kind)
    {
        var match = FindSingleMatch(content, anchor, path, editIndex, kind);
        CheckExpectedAnchorHash(edit, editIndex, path, kind, anchor);
        var index = before ? match.Index : match.Index + match.Length;
        return content[..index] + text + content[index..];
    }

    private static string ReplaceLines(string content, PatchEditOperation edit, int editIndex, string path)
    {
        const string kind = "replace_lines";
        var startLine = RequiredLine(edit.StartLine, editIndex, kind, "startLine", path);
        var endLine = RequiredLine(edit.EndLine, editIndex, kind, "endLine", path);
        var oldRangeHash = Required(edit.OldRangeHash, editIndex, kind, "oldRangeHash", path);
        var newText = Required(edit.NewText, editIndex, kind, "newText", path);

        if (startLine < 1)
            throw InvalidLineRange(editIndex, kind, path, "startLine must be greater than or equal to 1.");
        if (endLine < startLine)
            throw InvalidLineRange(editIndex, kind, path, "endLine must be greater than or equal to startLine.");

        var range = FindLineRange(content, startLine, endLine, path, editIndex, kind);
        var actualHash = HashText(content.Substring(range.Start, range.Length));
        if (!ContentHashPattern.IsMatch(oldRangeHash))
            throw new PatchValidationException(
                "invalid_content_hash",
                $"oldRangeHash for '{path}' at edits[{editIndex}] must use the format {ContentHashFormat}.",
                path: path,
                editIndex: editIndex,
                kind: kind,
                hashField: "oldRangeHash",
                hashTarget: "lineRange",
                expectedFormat: ContentHashFormat);

        if (!string.Equals(actualHash, oldRangeHash, StringComparison.OrdinalIgnoreCase))
            throw new PatchValidationException(
                "edit_range_hash_mismatch",
                $"oldRangeHash mismatch for '{path}' at edits[{editIndex}]. Expected {oldRangeHash}, actual {actualHash}.",
                path: path,
                editIndex: editIndex,
                kind: kind,
                hashField: "oldRangeHash",
                expectedHash: oldRangeHash,
                actualHash: actualHash,
                hashTarget: "lineRange");

        return content[..range.Start] + newText + content[(range.Start + range.Length)..];
    }

    private string JsonSet(string content, PatchEditOperation edit, int editIndex, string path)
    {
        const string kind = "json_set";
        var pointer = Required(edit.Pointer, editIndex, kind, "pointer", path);
        if (!edit.ValueSpecified)
            throw new PatchValidationException(
                "invalid_parameters",
                $"edits[{editIndex}] kind '{kind}' requires value.",
                path: path,
                editIndex: editIndex,
                kind: kind);

        var segments = ParseJsonPointer(pointer, path, editIndex, kind);
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(content);
        }
        catch (JsonException ex)
        {
            throw new PatchValidationException(
                "invalid_parameters",
                $"File '{path}' is not valid JSON for edits[{editIndex}]: {ex.Message}",
                path: path,
                editIndex: editIndex,
                kind: kind);
        }

        var replacement = edit.Value?.DeepClone();
        if (segments.Count == 0)
        {
            root = replacement;
        }
        else
        {
            SetJsonPointerValue(root, segments, replacement, path, editIndex, kind);
        }

        _warnings.Add(new PatchWarning
        {
            Code = "json_formatting_changed",
            Message = "json_set rewrites JSON with System.Text.Json formatting and does not preserve comments.",
            Path = path,
            EditIndex = editIndex,
            Kind = kind,
        });

        return root?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";
    }

    private void ApplyReplaceSymbolSource(PatchEditOperation edit, int editIndex)
    {
        const string kind = "replace_symbol_source";
        if (_roslyn is null)
            throw new PatchValidationException(
                "workspace_unavailable",
                "replace_symbol_source requires an available Roslyn workspace.",
                editIndex: editIndex,
                kind: kind);

        var newText = Required(edit.NewText, editIndex, kind, "newText", edit.Path ?? "");
        var oldSourceHash = Required(edit.OldSourceHash, editIndex, kind, "oldSourceHash", edit.Path ?? "");
        if (!ContentHashPattern.IsMatch(oldSourceHash))
            throw new PatchValidationException(
                "invalid_content_hash",
                $"oldSourceHash at edits[{editIndex}] must use the format {ContentHashFormat}.",
                path: edit.Path,
                editIndex: editIndex,
                kind: kind,
                hashField: "oldSourceHash",
                hashTarget: "source",
                expectedFormat: ContentHashFormat);

        var source = ResolveSymbolSource(edit, editIndex, kind);
        if (source.Source is null)
            throw new PatchValidationException(
                "semantic_symbol_not_found",
                $"Resolved symbol does not have source text at edits[{editIndex}].",
                path: edit.Path,
                editIndex: editIndex,
                kind: kind);

        var path = NormalizePath(source.Source.Path);
        var state = GetBuffer(path);
        RequireExistingFile(state, path, editIndex, kind);
        CheckExpectedFileHash(edit with { Kind = kind }, editIndex, path, state.Content!);

        var actualSourceHash = HashText(source.Source.Text);
        if (!string.Equals(actualSourceHash, oldSourceHash, StringComparison.OrdinalIgnoreCase))
            throw new PatchValidationException(
                "semantic_span_hash_mismatch",
                $"oldSourceHash mismatch for '{path}' at edits[{editIndex}]. Expected {oldSourceHash}, actual {actualSourceHash}.",
                path: path,
                editIndex: editIndex,
                kind: kind,
                hashField: "oldSourceHash",
                expectedHash: oldSourceHash,
                actualHash: actualSourceHash,
                hashTarget: "source");

        var start = OffsetFromLineColumn(state.Content!, source.Source.StartLine, source.Source.StartColumn, path, editIndex, kind);
        if (start + source.Source.Text.Length > state.Content!.Length ||
            !string.Equals(state.Content.Substring(start, source.Source.Text.Length), source.Source.Text, StringComparison.Ordinal))
        {
            throw new PatchValidationException(
                "semantic_span_hash_mismatch",
                $"Resolved symbol source no longer matches '{path}' at edits[{editIndex}].",
                path: path,
                editIndex: editIndex,
                kind: kind,
                hashField: "oldSourceHash",
                expectedHash: oldSourceHash,
                actualHash: HashText(source.Source.Text),
                hashTarget: "source");
        }

        state.Content = state.Content[..start] + newText + state.Content[(start + source.Source.Text.Length)..];
    }

    private GetSymbolSourceResult ResolveSymbolSource(PatchEditOperation edit, int editIndex, string kind)
    {
        var hasSymbolId = !string.IsNullOrWhiteSpace(edit.SymbolId);
        var hasName = !string.IsNullOrWhiteSpace(edit.Name);
        var hasLocation = !string.IsNullOrWhiteSpace(edit.Path) || edit.Line is not null || edit.Column is not null;
        var selectorCount = (hasSymbolId ? 1 : 0) + (hasName ? 1 : 0) + (hasLocation ? 1 : 0);
        if (selectorCount == 0)
            throw new PatchValidationException("invalid_parameters", $"edits[{editIndex}] kind '{kind}' requires symbolId, name, or path/line/column.", path: edit.Path, editIndex: editIndex, kind: kind);
        if (selectorCount > 1)
            throw new PatchValidationException("invalid_parameters", $"edits[{editIndex}] kind '{kind}' requires exactly one selector: symbolId, name, or path/line/column.", path: edit.Path, editIndex: editIndex, kind: kind);
        if (hasLocation && (string.IsNullOrWhiteSpace(edit.Path) || edit.Line is null || edit.Column is null))
            throw new PatchValidationException("invalid_parameters", $"edits[{editIndex}] kind '{kind}' requires path, line, and column together.", path: edit.Path, editIndex: editIndex, kind: kind);

        try
        {
            return _roslyn!.GetSymbolSource(new GetSymbolSourceQuery
            {
                SymbolId = edit.SymbolId,
                Name = edit.Name,
                Match = edit.Match,
                Kinds = edit.Kinds,
                Project = edit.Project,
                IncludeNonPublic = edit.IncludeNonPublic,
                RelativePath = edit.Path,
                Line = edit.Line,
                Column = edit.Column,
            });
        }
        catch (SymbolNotFoundException ex)
        {
            throw new PatchValidationException(
                "semantic_symbol_not_found",
                ex.Message,
                path: edit.Path,
                editIndex: editIndex,
                kind: kind);
        }
        catch (ArgumentException ex) when (ex.Message.Contains("matched multiple symbols", StringComparison.OrdinalIgnoreCase))
        {
            throw new PatchValidationException(
                "semantic_symbol_not_unique",
                ex.Message,
                path: edit.Path,
                editIndex: editIndex,
                kind: kind);
        }
    }

    private static int OffsetFromLineColumn(string content, int line, int column, string path, int editIndex, string kind)
    {
        if (line < 1 || column < 1)
            throw InvalidSemanticSpan(path, editIndex, kind);

        var currentLine = 1;
        var currentColumn = 1;
        for (var i = 0; i < content.Length; i++)
        {
            if (currentLine == line && currentColumn == column)
                return i;

            if (content[i] == '\n')
            {
                currentLine++;
                currentColumn = 1;
            }
            else
            {
                currentColumn++;
            }
        }

        if (currentLine == line && currentColumn == column)
            return content.Length;

        throw InvalidSemanticSpan(path, editIndex, kind);
    }

    private static PatchValidationException InvalidSemanticSpan(string path, int editIndex, string kind) =>
        new(
            "semantic_span_hash_mismatch",
            $"Resolved symbol source span is outside '{path}' at edits[{editIndex}].",
            path: path,
            editIndex: editIndex,
            kind: kind,
            hashField: "oldSourceHash",
            hashTarget: "source");

    private static IReadOnlyList<string> ParseJsonPointer(string pointer, string path, int editIndex, string kind)
    {
        if (pointer.Length == 0)
            return [];
        if (!pointer.StartsWith("/", StringComparison.Ordinal))
            throw new PatchValidationException(
                "invalid_parameters",
                $"edits[{editIndex}] kind '{kind}' pointer for '{path}' must be empty or start with '/'.",
                path: path,
                editIndex: editIndex,
                kind: kind);

        return pointer[1..].Split('/').Select(segment => DecodeJsonPointerSegment(segment, path, editIndex, kind)).ToArray();
    }

    private static string DecodeJsonPointerSegment(string segment, string path, int editIndex, string kind)
    {
        var builder = new StringBuilder(segment.Length);
        for (var i = 0; i < segment.Length; i++)
        {
            if (segment[i] != '~')
            {
                builder.Append(segment[i]);
                continue;
            }

            if (i + 1 >= segment.Length)
                throw InvalidJsonPointerEscape(path, editIndex, kind);

            var escaped = segment[++i];
            builder.Append(escaped switch
            {
                '0' => '~',
                '1' => '/',
                _ => throw InvalidJsonPointerEscape(path, editIndex, kind),
            });
        }

        return builder.ToString();
    }

    private static PatchValidationException InvalidJsonPointerEscape(string path, int editIndex, string kind) =>
        new(
            "invalid_parameters",
            $"edits[{editIndex}] kind '{kind}' pointer for '{path}' contains an invalid JSON Pointer escape.",
            path: path,
            editIndex: editIndex,
            kind: kind);

    private static void SetJsonPointerValue(JsonNode? root, IReadOnlyList<string> segments, JsonNode? value, string path, int editIndex, string kind)
    {
        var parent = ResolveJsonPointerParent(root, segments, path, editIndex, kind);
        var leaf = segments[^1];
        switch (parent)
        {
            case JsonObject obj:
                if (!obj.ContainsKey(leaf))
                    throw JsonPointerNotFound(path, editIndex, kind, segments);
                obj[leaf] = value;
                return;
            case JsonArray array:
                if (!int.TryParse(leaf, out var index) || index < 0 || index >= array.Count)
                    throw JsonPointerNotFound(path, editIndex, kind, segments);
                array[index] = value;
                return;
            default:
                throw JsonPointerNotFound(path, editIndex, kind, segments);
        }
    }

    private static JsonNode? ResolveJsonPointerParent(JsonNode? root, IReadOnlyList<string> segments, string path, int editIndex, string kind)
    {
        var current = root;
        for (var i = 0; i < segments.Count - 1; i++)
        {
            current = current switch
            {
                JsonObject obj when obj.TryGetPropertyValue(segments[i], out var child) => child,
                JsonArray array when int.TryParse(segments[i], out var index) && index >= 0 && index < array.Count => array[index],
                _ => throw JsonPointerNotFound(path, editIndex, kind, segments),
            };
        }

        return current;
    }

    private static PatchValidationException JsonPointerNotFound(string path, int editIndex, string kind, IReadOnlyList<string> segments) =>
        new(
            "edit_anchor_not_found",
            $"JSON pointer '/{string.Join("/", segments.Select(EncodeJsonPointerSegment))}' was not found in '{path}' at edits[{editIndex}].",
            path: path,
            editIndex: editIndex,
            kind: kind,
            matchCount: 0);

    private static string EncodeJsonPointerSegment(string segment) =>
        segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static LineRange FindLineRange(string content, int startLine, int endLine, string path, int editIndex, string kind)
    {
        var lineNumber = 1;
        var index = 0;
        int? start = null;

        while (index < content.Length)
        {
            var lineStart = index;
            var lineEndExclusive = NextLineEndExclusive(content, lineStart);

            if (lineNumber == startLine)
                start = lineStart;
            if (lineNumber == endLine)
            {
                if (start is null)
                    break;

                return new LineRange(start.Value, lineEndExclusive - start.Value);
            }

            index = lineEndExclusive;
            lineNumber++;
        }

        if (index == content.Length && content.Length > 0 && EndsWithLineTerminator(content))
        {
            if (startLine == lineNumber && endLine == lineNumber)
                return new LineRange(content.Length, 0);
        }

        throw new PatchValidationException(
            "invalid_parameters",
            $"replace_lines range {startLine}-{endLine} is outside '{path}' at edits[{editIndex}].",
            path: path,
            editIndex: editIndex,
            kind: kind);
    }

    private static int NextLineEndExclusive(string content, int lineStart)
    {
        var index = lineStart;
        while (index < content.Length)
        {
            var ch = content[index++];
            if (ch == '\n')
                break;
        }

        return index;
    }

    private static bool EndsWithLineTerminator(string content) =>
        content.EndsWith('\n');

    private static MatchLocation FindSingleMatch(string content, string needle, string path, int editIndex, string kind)
    {
        if (needle.Length == 0)
            throw new PatchValidationException(
                "invalid_parameters",
                $"Edit target text for '{path}' at edits[{editIndex}] must not be empty.",
                path: path,
                editIndex: editIndex,
                kind: kind);

        var matches = FindExactMatches(content, needle);
        if (matches.Count == 0)
            matches = FindNewlineFlexibleMatches(content, needle);

        if (matches.Count == 0)
            throw new PatchValidationException(
                "edit_anchor_not_found",
                $"Edit target was not found in '{path}' at edits[{editIndex}]. Match count: 0.",
                path: path,
                editIndex: editIndex,
                kind: kind,
                matchCount: 0,
                lineEndingHint: LineEndingHint(content, needle));

        if (matches.Count > 1)
            throw new PatchValidationException(
                "edit_anchor_not_unique",
                $"Edit target was not unique in '{path}' at edits[{editIndex}]. Match count: {matches.Count}.",
                path: path,
                editIndex: editIndex,
                kind: kind,
                matchCount: matches.Count,
                matches: matches.Locations);

        return new MatchLocation(matches.FirstIndex, matches.FirstLength);
    }

    private static MatchSearchResult FindExactMatches(string content, string needle)
    {
        var count = 0;
        var locations = new List<PatchEditMatchLocation>();
        var firstLocation = -1;
        var firstLength = 0;
        var index = 0;
        while (true)
        {
            index = content.IndexOf(needle, index, StringComparison.Ordinal);
            if (index < 0)
                return new MatchSearchResult(count, firstLocation, firstLength, locations);

            if (firstLocation < 0)
            {
                firstLocation = index;
                firstLength = needle.Length;
            }
            count++;
            if (locations.Count < MaxMatchLocations)
                locations.Add(ToMatchLocation(content, index));

            index += needle.Length;
        }
    }

    private static MatchSearchResult FindNewlineFlexibleMatches(string content, string needle)
    {
        var count = 0;
        var locations = new List<PatchEditMatchLocation>();
        var firstLocation = -1;
        var firstLength = 0;
        var index = 0;

        while (index < content.Length)
        {
            var length = NewlineFlexibleMatchLength(content, needle, index);
            if (length is not null)
            {
                if (firstLocation < 0)
                {
                    firstLocation = index;
                    firstLength = length.Value;
                }

                count++;
                if (locations.Count < MaxMatchLocations)
                    locations.Add(ToMatchLocation(content, index));

                index += Math.Max(1, length.Value);
                continue;
            }

            index++;
        }

        return new MatchSearchResult(count, firstLocation, firstLength, locations);
    }

    private static int? NewlineFlexibleMatchLength(string content, string needle, int contentStart)
    {
        var contentIndex = contentStart;
        var needleIndex = 0;

        while (needleIndex < needle.Length)
        {
            if (contentIndex >= content.Length)
                return null;

            if (TryReadLineEnding(needle, needleIndex, out var needleLength))
            {
                if (!TryReadLineEnding(content, contentIndex, out var contentLength))
                    return null;

                needleIndex += needleLength;
                contentIndex += contentLength;
                continue;
            }

            if (content[contentIndex] != needle[needleIndex])
                return null;

            contentIndex++;
            needleIndex++;
        }

        return contentIndex - contentStart;
    }

    private static bool TryReadLineEnding(string text, int index, out int length)
    {
        length = 0;
        if (index >= text.Length)
            return false;

        if (text[index] == '\r')
        {
            length = index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1;
            return true;
        }

        if (text[index] == '\n')
        {
            length = 1;
            return true;
        }

        return false;
    }

    private static string? LineEndingHint(string content, string needle)
    {
        var contentEnding = DominantLineEnding(content);
        var needleEnding = DominantLineEnding(needle);

        return (contentEnding, needleEnding) switch
        {
            ("crlf", "lf") => "file_uses_crlf_anchor_uses_lf",
            ("lf", "crlf") => "file_uses_lf_anchor_uses_crlf",
            ("cr", "lf") => "file_uses_cr_anchor_uses_lf",
            ("lf", "cr") => "file_uses_lf_anchor_uses_cr",
            ("crlf", "cr") => "file_uses_crlf_anchor_uses_cr",
            ("cr", "crlf") => "file_uses_cr_anchor_uses_crlf",
            _ => null,
        };
    }

    private static string? DominantLineEnding(string text)
    {
        var crlf = 0;
        var lf = 0;
        var cr = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                {
                    cr++;
                }
            }
            else if (text[i] == '\n')
            {
                lf++;
            }
        }

        if (crlf == 0 && lf == 0 && cr == 0)
            return null;
        if (crlf >= lf && crlf >= cr)
            return "crlf";
        return lf >= cr ? "lf" : "cr";
    }

    private static PatchEditMatchLocation ToMatchLocation(string content, int index)
    {
        var line = 1;
        var column = 0;
        for (var i = 0; i < index; i++)
        {
            if (content[i] == '\n')
            {
                line++;
                column = 0;
            }
            else
            {
                column++;
            }
        }

        return new PatchEditMatchLocation(line, column);
    }

    private BufferState GetBuffer(string normalizedPath)
    {
        if (_buffers.TryGetValue(normalizedPath, out var state))
            return state;

        var abs = _sandbox.ResolveAbsolute(normalizedPath);
        if (Directory.Exists(abs))
            throw new PatchValidationException("invalid_patch", $"Patch path is a directory: {normalizedPath}");

        if (File.Exists(abs))
        {
            state = new BufferState(
                normalizedPath,
                existedAtStart: true,
                originalHash: ContentHash.ForFile(abs),
                content: File.ReadAllText(abs));
        }
        else
        {
            state = new BufferState(
                normalizedPath,
                existedAtStart: false,
                originalHash: null,
                content: null);
        }

        _buffers.Add(normalizedPath, state);
        return state;
    }

    private static PatchFileOperation ToOperation(BufferState state)
    {
        if (state.Deleted)
        {
            return new PatchFileOperation
            {
                Path = state.Path,
                Operation = PatchFileOperationKind.Delete,
                OldContentHash = state.OriginalHash,
            };
        }

        return new PatchFileOperation
        {
            Path = state.Path,
            Operation = state.ExistedAtStart ? PatchFileOperationKind.Replace : PatchFileOperationKind.Create,
            OldContentHash = state.ExistedAtStart ? state.OriginalHash : null,
            NewContent = state.Content,
        };
    }

    private static void RequireExistingFile(BufferState state, string normalizedPath, int? editIndex = null, string? kind = null)
    {
        if (!state.Exists)
            throw new PatchValidationException(
                "file_not_found",
                editIndex is null ? $"File not found: {normalizedPath}" : $"File not found: {normalizedPath} at edits[{editIndex}].",
                path: editIndex is null ? null : normalizedPath,
                editIndex: editIndex,
                kind: kind);
    }

    private static void RequireMatchingHash(string relativePath, string content, string? expectedHash)
    {
        if (string.IsNullOrWhiteSpace(expectedHash))
            throw new PatchValidationException("missing_content_hash", $"Operation for '{relativePath}' requires oldContentHash.");
        if (!ContentHashPattern.IsMatch(expectedHash))
            throw new PatchValidationException(
                "invalid_content_hash",
                $"oldContentHash for '{relativePath}' must use the format sha256:<64 lowercase hex characters>.");

        var actual = HashText(content);
        if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new PatchValidationException(
                "content_hash_mismatch",
                $"Content hash mismatch for '{relativePath}'. Expected {expectedHash}, actual {actual}.");
    }

    private static void CheckExpectedFileHash(PatchEditOperation edit, int editIndex, string normalizedPath, string content)
    {
        if (string.IsNullOrWhiteSpace(edit.ExpectedFileHash))
            return;
        if (!ContentHashPattern.IsMatch(edit.ExpectedFileHash))
            throw new PatchValidationException(
                "invalid_content_hash",
                $"expectedFileHash for '{normalizedPath}' at edits[{editIndex}] must use the format {ContentHashFormat}.",
                path: normalizedPath,
                editIndex: editIndex,
                kind: edit.Kind,
                hashField: "expectedFileHash",
                hashTarget: "file",
                expectedFormat: ContentHashFormat);

        var actual = HashText(content);
        if (!string.Equals(actual, edit.ExpectedFileHash, StringComparison.OrdinalIgnoreCase))
            throw new PatchValidationException(
                "edit_conflict",
                $"expectedFileHash mismatch for '{normalizedPath}' at edits[{editIndex}]. Expected {edit.ExpectedFileHash}, actual {actual}.",
                path: normalizedPath,
                editIndex: editIndex,
                kind: edit.Kind,
                hashField: "expectedFileHash",
                expectedHash: edit.ExpectedFileHash,
                actualHash: actual,
                hashTarget: "file");
    }

    private static void CheckExpectedAnchorHash(PatchEditOperation edit, int editIndex, string path, string kind, string matchedText)
    {
        if (string.IsNullOrWhiteSpace(edit.ExpectedAnchorHash))
            return;
        if (!ContentHashPattern.IsMatch(edit.ExpectedAnchorHash))
            throw new PatchValidationException(
                "invalid_content_hash",
                $"expectedAnchorHash for '{path}' at edits[{editIndex}] must use the format {ContentHashFormat}.",
                path: path,
                editIndex: editIndex,
                kind: kind,
                hashField: "expectedAnchorHash",
                hashTarget: AnchorHashTarget(kind),
                expectedFormat: ContentHashFormat);

        var actual = HashText(matchedText);
        if (!string.Equals(actual, edit.ExpectedAnchorHash, StringComparison.OrdinalIgnoreCase))
            throw new PatchValidationException(
                "edit_conflict",
                $"expectedAnchorHash mismatch for '{path}' at edits[{editIndex}]. Expected {edit.ExpectedAnchorHash}, actual {actual}.",
                path: path,
                editIndex: editIndex,
                kind: kind,
                hashField: "expectedAnchorHash",
                expectedHash: edit.ExpectedAnchorHash,
                actualHash: actual,
                hashTarget: AnchorHashTarget(kind));
    }

    private static string AnchorHashTarget(string kind) =>
        kind is "replace_exact" or "delete_exact" ? "oldText" : "anchor";

    private static string Required(string? value, int editIndex, string kind, string field, string path)
    {
        if (value is null)
            throw new PatchValidationException(
                "invalid_parameters",
                $"edits[{editIndex}] kind '{kind}' requires {field}.",
                path: path,
                editIndex: editIndex,
                kind: kind);

        return value;
    }

    private static int RequiredLine(int? value, int editIndex, string kind, string field, string path)
    {
        if (value is null)
            throw new PatchValidationException(
                "invalid_parameters",
                $"edits[{editIndex}] kind '{kind}' requires {field}.",
                path: path,
                editIndex: editIndex,
                kind: kind);

        return value.Value;
    }

    private static PatchValidationException InvalidLineRange(int editIndex, string kind, string path, string message) =>
        new(
            "invalid_parameters",
            $"edits[{editIndex}] kind '{kind}' has invalid line range for '{path}': {message}",
            path: path,
            editIndex: editIndex,
            kind: kind);

    private static string HashText(string text) =>
        ContentHash.ForBytes(Encoding.UTF8.GetBytes(text));

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    private sealed class BufferState
    {
        public BufferState(string path, bool existedAtStart, string? originalHash, string? content)
        {
            Path = path;
            ExistedAtStart = existedAtStart;
            OriginalHash = originalHash;
            Content = content;
        }

        public string Path { get; }

        public bool ExistedAtStart { get; }

        public string? OriginalHash { get; }

        public string? Content { get; set; }

        public bool Deleted { get; set; }

        public bool Exists => Content is not null && !Deleted;
    }

    private sealed record MatchSearchResult(
        int Count,
        int FirstIndex,
        int FirstLength,
        IReadOnlyList<PatchEditMatchLocation> Locations);

    private readonly record struct MatchLocation(int Index, int Length);

    private readonly record struct LineRange(int Start, int Length);
}
