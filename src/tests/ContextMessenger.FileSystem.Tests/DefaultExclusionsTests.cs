namespace ContextMessenger.FileSystem.Tests;

public sealed class DefaultExclusionsTests
{
    [Theory]
    [InlineData(".git")]
    [InlineData(".vs")]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData("packages")]
    [InlineData("TestResults")]
    [InlineData("node_modules")]
    public void IsExcludedDirectoryName_recognises_defaults(string name)
    {
        Assert.True(DefaultExclusions.IsExcludedDirectoryName(name));
    }

    [Fact]
    public void IsExcludedDirectoryName_is_case_insensitive()
    {
        Assert.True(DefaultExclusions.IsExcludedDirectoryName("BIN"));
        Assert.True(DefaultExclusions.IsExcludedDirectoryName("Obj"));
    }

    [Fact]
    public void IsExcludedDirectoryName_false_for_normal_dir()
    {
        Assert.False(DefaultExclusions.IsExcludedDirectoryName("src"));
        Assert.False(DefaultExclusions.IsExcludedDirectoryName("tests"));
    }
}
