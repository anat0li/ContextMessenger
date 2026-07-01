using ContextMessenger.Core.Patching;
using ContextMessenger.FileSystem;

namespace ContextMessenger.Patching.Tests;

public sealed class FilePatchApplierTests
{
    [Fact]
    public void Apply_replaces_creates_and_deletes_files_after_validation()
    {
        using var temp = new TempDirectory();
        var replacePath = temp.CreateFile("src/replace.txt", "old");
        var deletePath = temp.CreateFile("src/delete.txt", "remove");
        var applier = new FilePatchApplier(new PathSandbox(temp.Path));

        var result = applier.Apply(
        [
            new PatchFileOperation
            {
                Path = "src/replace.txt",
                Operation = PatchFileOperationKind.Replace,
                OldContentHash = ContentHash.ForFile(replacePath),
                NewContent = "new",
            },
            new PatchFileOperation
            {
                Path = "src/create.txt",
                Operation = PatchFileOperationKind.Create,
                NewContent = "created",
            },
            new PatchFileOperation
            {
                Path = "src/delete.txt",
                Operation = PatchFileOperationKind.Delete,
                OldContentHash = ContentHash.ForFile(deletePath),
            },
        ]);

        Assert.Equal(["src/replace.txt", "src/create.txt", "src/delete.txt"], result.ChangedFiles);
        Assert.Equal("new", File.ReadAllText(System.IO.Path.Combine(temp.Path, "src", "replace.txt")));
        Assert.Equal("created", File.ReadAllText(System.IO.Path.Combine(temp.Path, "src", "create.txt")));
        Assert.False(File.Exists(deletePath));
    }

    [Fact]
    public void Apply_replace_preserves_crlf_line_endings()
    {
        using var temp = new TempDirectory();
        var path = temp.CreateFile("file.txt", "alpha\r\nbeta\r\n");
        var applier = new FilePatchApplier(new PathSandbox(temp.Path));

        applier.Apply(
        [
            new PatchFileOperation
            {
                Path = "file.txt",
                Operation = PatchFileOperationKind.Replace,
                OldContentHash = ContentHash.ForFile(path),
                NewContent = "alpha\nGAMMA\n", // model emits LF; the file's CRLF must be preserved
            },
        ]);

        Assert.Equal("alpha\r\nGAMMA\r\n", File.ReadAllText(path));
    }

    [Fact]
    public void Apply_replace_preserves_utf8_bom()
    {
        using var temp = new TempDirectory();
        var path = System.IO.Path.Combine(temp.Path, "bom.txt");
        File.WriteAllText(path, "first\n", new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var applier = new FilePatchApplier(new PathSandbox(temp.Path));

        applier.Apply(
        [
            new PatchFileOperation
            {
                Path = "bom.txt",
                Operation = PatchFileOperationKind.Replace,
                OldContentHash = ContentHash.ForFile(path),
                NewContent = "second\n",
            },
        ]);

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal("second\n", new System.Text.UTF8Encoding(false).GetString(bytes[3..]));
    }

    [Fact]
    public void Apply_replace_keeps_lf_and_no_bom_for_plain_utf8_files()
    {
        using var temp = new TempDirectory();
        var path = temp.CreateFile("plain.txt", "one\ntwo\n");
        var applier = new FilePatchApplier(new PathSandbox(temp.Path));

        applier.Apply(
        [
            new PatchFileOperation
            {
                Path = "plain.txt",
                Operation = PatchFileOperationKind.Replace,
                OldContentHash = ContentHash.ForFile(path),
                NewContent = "one\nTWO\n",
            },
        ]);

        var bytes = File.ReadAllBytes(path);
        Assert.NotEqual((byte)0xEF, bytes[0]); // no BOM introduced
        Assert.Equal("one\nTWO\n", new System.Text.UTF8Encoding(false).GetString(bytes));
    }

    [Fact]
    public void Apply_create_writes_utf8_without_bom()
    {
        using var temp = new TempDirectory();
        var applier = new FilePatchApplier(new PathSandbox(temp.Path));

        applier.Apply(
        [
            new PatchFileOperation
            {
                Path = "fresh.txt",
                Operation = PatchFileOperationKind.Create,
                NewContent = "hello\n",
            },
        ]);

        var bytes = File.ReadAllBytes(System.IO.Path.Combine(temp.Path, "fresh.txt"));
        Assert.NotEqual((byte)0xEF, bytes[0]);
        Assert.Equal("hello\n", new System.Text.UTF8Encoding(false).GetString(bytes));
    }

    [Fact]
    public void Apply_hash_mismatch_rejects_patch_without_writing_later_operations()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("src/replace.txt", "old");
        temp.CreateFile("src/create.txt", "preexisting");
        var applier = new FilePatchApplier(new PathSandbox(temp.Path));

        var ex = Assert.Throws<PatchValidationException>(() => applier.Apply(
        [
            new PatchFileOperation
            {
                Path = "src/replace.txt",
                Operation = PatchFileOperationKind.Replace,
                OldContentHash = "sha256:" + new string('0', 64),
                NewContent = "new",
            },
            new PatchFileOperation
            {
                Path = "src/create2.txt",
                Operation = PatchFileOperationKind.Create,
                NewContent = "created",
            },
        ]));

        Assert.Equal("content_hash_mismatch", ex.Code);
        Assert.Equal("old", File.ReadAllText(System.IO.Path.Combine(temp.Path, "src", "replace.txt")));
        Assert.False(File.Exists(System.IO.Path.Combine(temp.Path, "src", "create2.txt")));
    }

    [Fact]
    public void Apply_rejects_malformed_content_hash()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("src/replace.txt", "old");
        var applier = new FilePatchApplier(new PathSandbox(temp.Path));

        var ex = Assert.Throws<PatchValidationException>(() => applier.Apply(
        [
            new PatchFileOperation
            {
                Path = "src/replace.txt",
                Operation = PatchFileOperationKind.Replace,
                OldContentHash = "sha256:bad",
                NewContent = "new",
            },
        ]));

        Assert.Equal("invalid_content_hash", ex.Code);
    }

    [Fact]
    public void Apply_rejects_duplicate_paths()
    {
        using var temp = new TempDirectory();
        var applier = new FilePatchApplier(new PathSandbox(temp.Path));

        var ex = Assert.Throws<PatchValidationException>(() => applier.Apply(
        [
            new PatchFileOperation { Path = "src/file.txt", Operation = PatchFileOperationKind.Create, NewContent = "a" },
            new PatchFileOperation { Path = "src\\file.txt", Operation = PatchFileOperationKind.Create, NewContent = "b" },
        ]));

        Assert.Equal("duplicate_patch_path", ex.Code);
    }
}
