using System.Security.Cryptography;
using ContextMessenger.Core.FileSystem;

namespace ContextMessenger.FileSystem.Tests;

public sealed class FileReaderServiceTests
{
    [Fact]
    public void ReadFile_returns_full_content()
    {
        using var temp = new TempDirectory();
        var content = "line1\nline2\nline3";
        temp.CreateFile("file.txt", content);

        var reader = new FileReaderService(new PathSandbox(temp.Path));
        var result = reader.ReadFile("file.txt");

        Assert.Equal(content, result.Content);
        Assert.Equal(3, result.LineCount);
        Assert.False(result.IsTruncated);
        Assert.Equal("file.txt", result.RelativePath);
        Assert.StartsWith("sha256:", result.ContentHash);
        Assert.Equal(71, result.ContentHash.Length);
    }

    [Fact]
    public void ReadFile_counts_lines_with_trailing_newline_correctly()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("file.txt", "a\nb\nc\n");

        var reader = new FileReaderService(new PathSandbox(temp.Path));
        var result = reader.ReadFile("file.txt");

        Assert.Equal(3, result.LineCount);
    }

    [Fact]
    public void ReadFile_treats_empty_file_as_zero_lines()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("file.txt", "");

        var reader = new FileReaderService(new PathSandbox(temp.Path));
        var result = reader.ReadFile("file.txt");

        Assert.Equal(0, result.LineCount);
        Assert.Empty(result.Content);
    }

    [Fact]
    public void ReadFile_truncates_when_file_exceeds_max_bytes()
    {
        using var temp = new TempDirectory();
        var big = new string('a', 5_000);
        temp.CreateFile("big.txt", big);

        var reader = new FileReaderService(new PathSandbox(temp.Path));
        var result = reader.ReadFile("big.txt", maxBytes: 100);

        Assert.True(result.IsTruncated);
        Assert.Equal(100, result.Content.Length);
        Assert.Equal(5_000, result.ByteSize);
        Assert.StartsWith("sha256:", result.ContentHash);
        Assert.Equal(HashText(big), result.ContentHash);
    }

    [Fact]
    public void ReadFile_returns_specified_line_range()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("file.txt", "one\ntwo\nthree\nfour\nfive");

        var reader = new FileReaderService(new PathSandbox(temp.Path));
        var result = reader.ReadFile("file.txt", startLine: 2, endLine: 4);

        Assert.Equal("two\nthree\nfour\n", result.Content);
        Assert.Equal(3, result.LineCount);
        Assert.False(result.IsTruncated);
        Assert.Equal(HashText("one\ntwo\nthree\nfour\nfive"), result.ContentHash);
        Assert.Equal(HashText("two\nthree\nfour\n"), result.RangeHash);
        Assert.Equal(2, result.RangeStartLine);
        Assert.Equal(4, result.RangeEndLine);
        Assert.True(result.RangeIncludesEndLineTerminator);
        Assert.Equal("lf", result.LineEnding);
    }

    [Fact]
    public void ReadFile_range_preserves_crlf_for_replace_lines_hash()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("file.txt", "one\r\ntwo\r\nthree\r\nfour");

        var reader = new FileReaderService(new PathSandbox(temp.Path));
        var result = reader.ReadFile("file.txt", startLine: 2, endLine: 3);

        Assert.Equal("two\r\nthree\r\n", result.Content);
        Assert.Equal(HashText("two\r\nthree\r\n"), result.RangeHash);
        Assert.Equal(2, result.RangeStartLine);
        Assert.Equal(3, result.RangeEndLine);
        Assert.True(result.RangeIncludesEndLineTerminator);
        Assert.Equal("crlf", result.LineEnding);
    }

    [Fact]
    public void ReadFile_returns_empty_when_start_line_past_end()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("file.txt", "a\nb");

        var reader = new FileReaderService(new PathSandbox(temp.Path));
        var result = reader.ReadFile("file.txt", startLine: 100);

        Assert.Empty(result.Content);
        Assert.Equal(0, result.LineCount);
        Assert.Equal(HashText(""), result.RangeHash);
        Assert.Equal(100, result.RangeStartLine);
        Assert.Equal(99, result.RangeEndLine);
        Assert.False(result.RangeIncludesEndLineTerminator);
    }

    [Fact]
    public void ReadFile_throws_FileNotFound_when_missing()
    {
        using var temp = new TempDirectory();
        var reader = new FileReaderService(new PathSandbox(temp.Path));
        Assert.Throws<FileNotFoundException>(() => reader.ReadFile("ghost.txt"));
    }

    [Fact]
    public void ReadFile_throws_when_path_outside_sandbox()
    {
        using var temp = new TempDirectory();
        var reader = new FileReaderService(new PathSandbox(temp.Path));
        Assert.Throws<PathOutsideSandboxException>(() => reader.ReadFile("../escape.txt"));
    }

    [Fact]
    public void ReadFile_rejects_invalid_line_range()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("file.txt", "a\nb");
        var reader = new FileReaderService(new PathSandbox(temp.Path));

        Assert.Throws<ArgumentException>(() => reader.ReadFile("file.txt", startLine: 5, endLine: 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadFile("file.txt", startLine: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadFile("file.txt", endLine: 0));
    }

    private static string HashText(string text) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
