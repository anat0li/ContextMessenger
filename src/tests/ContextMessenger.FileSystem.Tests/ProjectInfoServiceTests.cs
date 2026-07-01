using ContextMessenger.Core.ProjectInfo;

namespace ContextMessenger.FileSystem.Tests;

public sealed class ProjectInfoServiceTests
{
    [Fact]
    public void GetProjectInfo_finds_solution_projects_tests_sdk_and_git_info()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("ContextMessenger.slnx", "<Solution />");
        temp.CreateFile("global.json", """{ "sdk": { "version": "10.0.100" } }""");
        temp.CreateFile(
            "src/App/App.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\Lib\Lib.csproj" />
              </ItemGroup>
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        temp.CreateFile(
            "src/Lib/Lib.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net9.0;net10.0</TargetFrameworks>
              </PropertyGroup>
            </Project>
            """);
        temp.CreateFile(
            "src/tests/App.Tests/App.Tests.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.7.0" />
              </ItemGroup>
            </Project>
            """);

        var git = new FakeGitProvider(new GitInfo
        {
            IsRepository = true,
            Branch = "main",
            HeadSha = "abc123",
            IsDirty = true,
        });
        var service = new ProjectInfoService(new PathSandbox(temp.Path), git);

        var info = service.GetProjectInfo();

        Assert.Equal(".", info.RootPath);
        Assert.Equal(["ContextMessenger.slnx"], info.SolutionFiles);
        Assert.Equal(3, info.ProjectFiles.Count);
        Assert.Equal("10.0.100", info.SdkVersion);
        Assert.True(info.Git!.IsRepository);
        Assert.Equal("main", info.Git.Branch);
        Assert.True(info.Git.IsDirty);

        var app = Assert.Single(info.ProjectFiles, p => p.Path == "src/App/App.csproj");
        Assert.Equal("App", app.Name);
        Assert.Equal("net10.0", app.TargetFramework);
        Assert.Null(app.TargetFrameworks);
        Assert.Equal(["src/Lib/Lib.csproj"], app.ProjectReferences);
        Assert.False(app.IsTestProject);

        var lib = Assert.Single(info.ProjectFiles, p => p.Path == "src/Lib/Lib.csproj");
        Assert.Equal(["net9.0", "net10.0"], lib.TargetFrameworks);
        Assert.Null(lib.TargetFramework);
        Assert.Null(lib.ProjectReferences);
        Assert.Null(lib.Packages);

        var test = Assert.Single(info.ProjectFiles, p => p.Path == "src/tests/App.Tests/App.Tests.csproj");
        Assert.Equal("src/tests/App.Tests/App.Tests.csproj", Assert.Single(info.TestProjects));
        Assert.True(test.IsTestProject);
        Assert.NotNull(test.Packages);
        var testSdk = Assert.Single(test.Packages);
        Assert.Equal("Microsoft.NET.Test.Sdk", testSdk.Name);
        Assert.Equal("18.7.0", testSdk.Version);
    }

    [Fact]
    public void GetProjectInfo_skips_default_excluded_directories()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("src/App/App.csproj", "<Project />");
        temp.CreateFile("bin/Skip/Skip.csproj", "<Project />");
        temp.CreateFile(".git/Skip/Skip.csproj", "<Project />");

        var service = new ProjectInfoService(new PathSandbox(temp.Path), new FakeGitProvider(new GitInfo()));

        var info = service.GetProjectInfo();

        var project = Assert.Single(info.ProjectFiles);
        Assert.Equal("src/App/App.csproj", project.Path);
    }

    [Fact]
    public void GetProjectInfo_extracts_package_references_with_attribute_and_element_versions_deduped()
    {
        using var temp = new TempDirectory();
        temp.CreateFile(
            "src/App/App.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
                <PackageReference Include="Serilog">
                  <Version>3.1.1</Version>
                </PackageReference>
                <PackageReference Include="CentralPackage" />
                <PackageReference Include="newtonsoft.json" Version="13.0.4" />
              </ItemGroup>
            </Project>
            """);

        var service = new ProjectInfoService(new PathSandbox(temp.Path), new FakeGitProvider(new GitInfo()));

        var info = service.GetProjectInfo();
        var project = Assert.Single(info.ProjectFiles);
        Assert.NotNull(project.Packages);
        Assert.Equal(3, project.Packages!.Count);

        var newtonsoft = Assert.Single(project.Packages, p =>
            string.Equals(p.Name, "Newtonsoft.Json", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("13.0.3", newtonsoft.Version);

        var serilog = Assert.Single(project.Packages, p => p.Name == "Serilog");
        Assert.Equal("3.1.1", serilog.Version);

        var central = Assert.Single(project.Packages, p => p.Name == "CentralPackage");
        Assert.Null(central.Version);
    }

    [Fact]
    public void GetProjectInfo_extracts_outputType_nullable_and_langVersion_when_set()
    {
        using var temp = new TempDirectory();
        temp.CreateFile(
            "src/App/App.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <LangVersion>preview</LangVersion>
              </PropertyGroup>
            </Project>
            """);

        var service = new ProjectInfoService(new PathSandbox(temp.Path), new FakeGitProvider(new GitInfo()));

        var project = Assert.Single(service.GetProjectInfo().ProjectFiles);
        Assert.Equal("Exe", project.OutputType);
        Assert.Equal("enable", project.Nullable);
        Assert.Equal("preview", project.LangVersion);
    }

    [Fact]
    public void GetProjectInfo_defaults_outputType_to_Library_when_not_specified()
    {
        using var temp = new TempDirectory();
        temp.CreateFile(
            "src/Lib/Lib.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var service = new ProjectInfoService(new PathSandbox(temp.Path), new FakeGitProvider(new GitInfo()));

        var project = Assert.Single(service.GetProjectInfo().ProjectFiles);
        Assert.Equal("Library", project.OutputType);
        Assert.Null(project.Nullable);
        Assert.Null(project.LangVersion);
    }

    [Fact]
    public void GetProjectInfo_preserves_WinExe_outputType()
    {
        using var temp = new TempDirectory();
        temp.CreateFile(
            "src/Wpf/Wpf.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>WinExe</OutputType>
                <TargetFramework>net10.0-windows</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var service = new ProjectInfoService(new PathSandbox(temp.Path), new FakeGitProvider(new GitInfo()));

        var project = Assert.Single(service.GetProjectInfo().ProjectFiles);
        Assert.Equal("WinExe", project.OutputType);
    }

    [Fact]
    public void GetProjectInfo_returns_null_packages_when_project_has_no_PackageReference()
    {
        using var temp = new TempDirectory();
        temp.CreateFile(
            "src/Lib/Lib.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var service = new ProjectInfoService(new PathSandbox(temp.Path), new FakeGitProvider(new GitInfo()));

        var info = service.GetProjectInfo();
        Assert.Null(Assert.Single(info.ProjectFiles).Packages);
    }

    [Fact]
    public void GetProjectInfo_detects_test_project_by_explicit_property()
    {
        using var temp = new TempDirectory();
        temp.CreateFile(
            "src/Spec/Spec.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <IsTestProject>true</IsTestProject>
              </PropertyGroup>
            </Project>
            """);

        var service = new ProjectInfoService(new PathSandbox(temp.Path), new FakeGitProvider(new GitInfo()));

        var info = service.GetProjectInfo();

        Assert.True(Assert.Single(info.ProjectFiles).IsTestProject);
        Assert.Equal("src/Spec/Spec.csproj", Assert.Single(info.TestProjects));
    }

    private sealed class FakeGitProvider : IGitRepositoryInfoProvider
    {
        private readonly GitInfo _info;

        public FakeGitProvider(GitInfo info)
        {
            _info = info;
        }

        public GitInfo GetGitInfo(string rootPath) => _info;
    }
}
