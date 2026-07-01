using ContextMessenger.Core.FileSystem;

namespace ContextMessenger.FileSystem.Tests;

public sealed class TextSearchServiceTests
{
    [Fact]
    public void SearchText_finds_literal_match_with_line_number_and_columns()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("a.cs", "first line\nhello world\nthird");

        var search = new TextSearchService(new PathSandbox(temp.Path));
        var matches = search.SearchText(new SearchQuery("hello"));

        var match = Assert.Single(matches);
        Assert.Equal("a.cs", match.RelativePath);
        Assert.Equal(2, match.LineNumber);
        Assert.Equal("hello world", match.LineText);
        Assert.Equal(0, match.ColumnStart);
        Assert.Equal(5, match.ColumnEnd);
    }

    [Fact]
    public void SearchText_is_case_insensitive_by_default()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("a.cs", "Hello WORLD");

        var search = new TextSearchService(new PathSandbox(temp.Path));
        var matches = search.SearchText(new SearchQuery("hello"));

        Assert.Single(matches);
    }

    [Fact]
    public void SearchText_respects_case_sensitivity_when_disabled()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("a.cs", "Hello WORLD");

        var search = new TextSearchService(new PathSandbox(temp.Path));
        var matches = search.SearchText(new SearchQuery("hello") { IgnoreCase = false });

        Assert.Empty(matches);
    }

    [Fact]
    public void SearchText_supports_regex()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("a.cs", "var foo123 = 1;");

        var search = new TextSearchService(new PathSandbox(temp.Path));
        var matches = search.SearchText(new SearchQuery(@"foo\d+") { IsRegex = true });

        var match = Assert.Single(matches);
        Assert.Equal("foo123", match.LineText.Substring(match.ColumnStart, match.ColumnEnd - match.ColumnStart));
    }

    [Fact]
    public void SearchText_skips_default_excluded_directories()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("src/file.cs", "needle");
        temp.CreateFile("bin/file.cs", "needle");
        temp.CreateFile(".git/file.cs", "needle");

        var search = new TextSearchService(new PathSandbox(temp.Path));
        var matches = search.SearchText(new SearchQuery("needle"));

        var paths = matches.Select(m => m.RelativePath).ToArray();
        Assert.Single(paths);
        Assert.Equal("src/file.cs", paths[0]);
    }

    [Fact]
    public void SearchText_filters_by_include_globs()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("a.cs", "needle");
        temp.CreateFile("a.txt", "needle");

        var search = new TextSearchService(new PathSandbox(temp.Path));
        var matches = search.SearchText(new SearchQuery("needle")
        {
            IncludeGlobs = ["**/*.cs"],
        });

        var match = Assert.Single(matches);
        Assert.Equal("a.cs", match.RelativePath);
    }

    [Fact]
    public void SearchText_applies_globs_relative_to_requested_path()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("src/app/keep.cs", "needle");
        temp.CreateFile("src/tests/skip.cs", "needle");
        temp.CreateFile("tests/outside.cs", "needle");

        var search = new TextSearchService(new PathSandbox(temp.Path));
        var matches = search.SearchText(new SearchQuery("needle")
        {
            RelativePath = "src",
            IncludeGlobs = ["**/*.cs"],
            ExcludeGlobs = ["tests/**"],
        });

        var match = Assert.Single(matches);
        Assert.Equal("src/app/keep.cs", match.RelativePath);
    }

    [Fact]
    public void SearchText_caps_results_at_max_results()
    {
        using var temp = new TempDirectory();
        for (int i = 0; i < 50; i++)
            temp.CreateFile($"file{i}.cs", "needle");

        var search = new TextSearchService(new PathSandbox(temp.Path));
        var matches = search.SearchText(new SearchQuery("needle") { MaxResults = 10 });

        Assert.Equal(10, matches.Count);
    }

    [Fact]
    public void SearchText_throws_for_empty_pattern()
    {
        using var temp = new TempDirectory();
        var search = new TextSearchService(new PathSandbox(temp.Path));
        Assert.Throws<ArgumentException>(() => search.SearchText(new SearchQuery("")));
    }

    [Fact]
    public void ListFiles_returns_relative_paths()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("a.cs", "x");
        temp.CreateFile("src/b.cs", "x");
        temp.CreateFile("src/inner/c.cs", "x");

        var search = new TextSearchService(new PathSandbox(temp.Path));
        var files = search.ListFiles(new ListFilesQuery());

        Assert.Equal(3, files.Count);
        Assert.Contains("a.cs", files);
        Assert.Contains("src/b.cs", files);
        Assert.Contains("src/inner/c.cs", files);
    }

    [Fact]
    public void ListFiles_returns_paths_ordered_by_relative_path()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("z.cs", "x");
        temp.CreateFile("src/c.cs", "x");
        temp.CreateFile("A.cs", "x");
        temp.CreateFile("src/b.cs", "x");

        var search = new TextSearchService(new PathSandbox(temp.Path));
        var files = search.ListFiles(new ListFilesQuery());

        Assert.Equal(["A.cs", "src/b.cs", "src/c.cs", "z.cs"], files);
    }

    [Fact]
    public void ListFiles_skips_default_excluded_directories()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("src/keep.cs", "x");
        temp.CreateFile("bin/skip.dll", "x");
        temp.CreateFile("obj/skip.cache", "x");

        var search = new TextSearchService(new PathSandbox(temp.Path));
        var files = search.ListFiles(new ListFilesQuery());

        Assert.Equal(new[] { "src/keep.cs" }, files);
    }

    [Fact]
    public void ListFiles_applies_include_and_exclude_globs()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("a.cs", "x");
        temp.CreateFile("a.txt", "x");
        temp.CreateFile("test/skip.cs", "x");

        var search = new TextSearchService(new PathSandbox(temp.Path));
        var files = search.ListFiles(new ListFilesQuery
        {
            IncludeGlobs = ["**/*.cs"],
            ExcludeGlobs = ["**/test/**"],
        });

        Assert.Equal(new[] { "a.cs" }, files);
    }

    [Fact]
    public void ListFiles_applies_globs_relative_to_requested_path()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("src/app/keep.cs", "x");
        temp.CreateFile("src/tests/skip.cs", "x");
        temp.CreateFile("tests/outside.cs", "x");

        var search = new TextSearchService(new PathSandbox(temp.Path));
        var files = search.ListFiles(new ListFilesQuery
        {
            RelativePath = "src",
            IncludeGlobs = ["**/*.cs"],
            ExcludeGlobs = ["**/tests/**"],
        });

        Assert.Equal(["src/app/keep.cs"], files);
    }

    [Fact]
    public void ListFiles_tolerates_leading_slash_globs_from_clients()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("src/app/keep.cs", "x");
        temp.CreateFile("src/tests/skip.cs", "x");
        temp.CreateFile("src/readme.md", "x");

        var search = new TextSearchService(new PathSandbox(temp.Path));
        var files = search.ListFiles(new ListFilesQuery
        {
            RelativePath = "src",
            IncludeGlobs = ["/*.cs"],
            ExcludeGlobs = ["/tests/**"],
        });

        Assert.Equal(["src/app/keep.cs"], files);
    }

    [Theory]
    [InlineData(".cs")]
    [InlineData("/.cs")]
    public void ListFiles_tolerates_extension_shorthand_include_from_clients(string includeGlob)
    {
        using var temp = new TempDirectory();
        temp.CreateFile("src/app/keep.cs", "x");
        temp.CreateFile("src/tests/skip.cs", "x");
        temp.CreateFile("src/readme.md", "x");

        var search = new TextSearchService(new PathSandbox(temp.Path));
        var files = search.ListFiles(new ListFilesQuery
        {
            RelativePath = "src",
            IncludeGlobs = [includeGlob],
            ExcludeGlobs = ["tests/"],
        });

        Assert.Equal(["src/app/keep.cs"], files);
    }

    [Theory]
    [InlineData("**/tests/**", 1)]
    [InlineData("tests/**", 1)]
    [InlineData("**/tests", 1)]
    [InlineData("/tests/**", 1)]
    [InlineData("src/tests/**", 2)]
    public void ListFiles_documents_exclude_globs_relative_to_requested_path(
        string excludeGlob,
        int expectedFileCount)
    {
        using var temp = new TempDirectory();
        temp.CreateFile("src/app/keep.cs", "x");
        temp.CreateFile("src/tests/skip.cs", "x");

        var search = new TextSearchService(new PathSandbox(temp.Path));
        var files = search.ListFiles(new ListFilesQuery
        {
            RelativePath = "src",
            IncludeGlobs = ["**/*.cs"],
            ExcludeGlobs = [excludeGlob],
        });

        Assert.Equal(expectedFileCount, files.Count);
        Assert.Contains("src/app/keep.cs", files);

        if (expectedFileCount == 1)
            Assert.DoesNotContain("src/tests/skip.cs", files);
        else
            Assert.Contains("src/tests/skip.cs", files);
    }
}
