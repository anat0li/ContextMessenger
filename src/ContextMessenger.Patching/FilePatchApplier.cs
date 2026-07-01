using System.Text;
using System.Text.RegularExpressions;
using ContextMessenger.Core.Patching;
using ContextMessenger.FileSystem;

namespace ContextMessenger.Patching;

public sealed class FilePatchApplier : IFilePatchApplier
{
    private static readonly Regex ContentHashPattern = new(
        "^sha256:[0-9a-f]{64}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly PathSandbox _sandbox;

    public FilePatchApplier(PathSandbox sandbox)
    {
        _sandbox = sandbox ?? throw new ArgumentNullException(nameof(sandbox));
    }

    public PatchApplyResult Apply(IReadOnlyList<PatchFileOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        Validate(operations);

        foreach (var op in operations)
            ApplyOne(op);

        return new PatchApplyResult
        {
            ChangedFiles = operations.Select(o => NormalizePath(o.Path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        };
    }

    public void Validate(IReadOnlyList<PatchFileOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var op in operations)
        {
            if (string.IsNullOrWhiteSpace(op.Path))
                throw new PatchValidationException("invalid_patch", "Patch operation path is required.");

            var normalized = NormalizePath(op.Path);
            if (!seen.Add(normalized))
                throw new PatchValidationException("duplicate_patch_path", $"Patch contains multiple operations for '{normalized}'.");

            var abs = _sandbox.ResolveForWrite(normalized);
            var exists = File.Exists(abs);

            switch (op.Operation)
            {
                case PatchFileOperationKind.Create:
                    if (exists)
                        throw new PatchValidationException("file_exists", $"Cannot create '{normalized}' because it already exists.");
                    if (op.NewContent is null)
                        throw new PatchValidationException("invalid_patch", $"Create operation for '{normalized}' requires newContent.");
                    break;

                case PatchFileOperationKind.Replace:
                    RequireExistingFile(normalized, abs, exists);
                    RequireMatchingHash(normalized, abs, op.OldContentHash);
                    if (op.NewContent is null)
                        throw new PatchValidationException("invalid_patch", $"Replace operation for '{normalized}' requires newContent.");
                    break;

                case PatchFileOperationKind.Delete:
                    RequireExistingFile(normalized, abs, exists);
                    RequireMatchingHash(normalized, abs, op.OldContentHash);
                    if (op.NewContent is not null)
                        throw new PatchValidationException("invalid_patch", $"Delete operation for '{normalized}' must not include newContent.");
                    break;

                default:
                    throw new PatchValidationException("invalid_patch", $"Unsupported patch operation '{op.Operation}'.");
            }
        }
    }

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private void ApplyOne(PatchFileOperation op)
    {
        var normalized = NormalizePath(op.Path);
        var abs = _sandbox.ResolveForWrite(normalized);
        switch (op.Operation)
        {
            case PatchFileOperationKind.Create:
                // No existing file to match; write the content as provided, UTF-8 without a BOM.
                WriteBytesAtomically(abs, Utf8NoBom.GetBytes(op.NewContent!));
                break;
            case PatchFileOperationKind.Replace:
                // Preserve the existing file's encoding (BOM) and dominant line ending so a replace
                // does not produce a whole-file diff purely from re-encoding or EOL changes.
                WriteBytesAtomically(abs, TextFormat.Detect(File.ReadAllBytes(abs)).Encode(op.NewContent!));
                break;
            case PatchFileOperationKind.Delete:
                File.Delete(abs);
                break;
        }
    }

    private static void WriteBytesAtomically(string absolutePath, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(absolutePath)!;
        Directory.CreateDirectory(directory);

        // Write to a sibling temp file then atomically swap it into place, so a crash mid-write
        // leaves the target either untouched or fully written - never half-written.
        var temp = Path.Combine(
            directory,
            "." + Path.GetFileName(absolutePath) + "." + Guid.NewGuid().ToString("N") + ".cmtmp");
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, absolutePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                TryDelete(temp);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup of the temp file after the atomic swap succeeded or failed.
        }
    }

    // Captures a text file's encoding (and whether it carries a byte-order mark) plus its dominant
    // line ending, so a replacement can be re-encoded to match the file it overwrites.
    private readonly record struct TextFormat(Encoding Encoding, string? Newline)
    {
        public static TextFormat Detect(byte[] original)
        {
            var encoding = DetectEncoding(original);
            return new TextFormat(encoding, DetectNewline(original, encoding));
        }

        public byte[] Encode(string content)
        {
            var body = Encoding.GetBytes(NormalizeNewlines(content));
            var preamble = Encoding.GetPreamble();
            if (preamble.Length == 0)
                return body;

            var result = new byte[preamble.Length + body.Length];
            Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
            Buffer.BlockCopy(body, 0, result, preamble.Length, body.Length);
            return result;
        }

        private string NormalizeNewlines(string content)
        {
            if (Newline is null)
                return content;

            var lf = content.Replace("\r\n", "\n").Replace("\r", "\n");
            return Newline == "\n" ? lf : lf.Replace("\n", Newline);
        }

        private static Encoding DetectEncoding(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode;
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode;

            return Utf8NoBom;
        }

        private static string? DetectNewline(byte[] bytes, Encoding encoding)
        {
            var text = encoding.GetString(bytes);
            int crlf = 0, lf = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] != '\n')
                    continue;
                if (i > 0 && text[i - 1] == '\r') crlf++;
                else lf++;
            }

            if (crlf == 0 && lf == 0)
                return null; // No newlines to match; leave the replacement content untouched.

            return crlf > lf ? "\r\n" : "\n";
        }
    }

    private static void RequireExistingFile(string relativePath, string absolutePath, bool exists)
    {
        if (!exists)
            throw new PatchValidationException("file_not_found", $"File not found: {relativePath}");
        if (Directory.Exists(absolutePath))
            throw new PatchValidationException("invalid_patch", $"Patch path is a directory: {relativePath}");
    }

    private static void RequireMatchingHash(string relativePath, string absolutePath, string? expectedHash)
    {
        if (string.IsNullOrWhiteSpace(expectedHash))
            throw new PatchValidationException("missing_content_hash", $"Operation for '{relativePath}' requires oldContentHash.");
        if (!ContentHashPattern.IsMatch(expectedHash))
            throw new PatchValidationException(
                "invalid_content_hash",
                $"oldContentHash for '{relativePath}' must use the format sha256:<64 lowercase hex characters>.");

        var actual = ContentHash.ForFile(absolutePath);
        if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new PatchValidationException(
                "content_hash_mismatch",
                $"Content hash mismatch for '{relativePath}'. Expected {expectedHash}, actual {actual}.");
    }

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');
}
