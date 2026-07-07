using Luma.App.Services;

namespace Luma.Tests;

public sealed class TestCommandDetectorTests
{
    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LumaTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void EmptyDirectoryDetectsNothing()
    {
        var dir = CreateTempDir();
        try { Assert.Null(TestCommandDetector.Detect(dir)); }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CargoTomlDetectsCargoTest()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "Cargo.toml"), "[package]\nname = \"x\"");
            Assert.Equal("cargo test", TestCommandDetector.Detect(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void GoModDetectsGoTest()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "go.mod"), "module example.com/x");
            Assert.Equal("go test ./...", TestCommandDetector.Detect(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void PackageJsonWithTestScriptDetectsNpmTest()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "package.json"), "{\"scripts\": {\"test\": \"jest\"}}");
            Assert.Equal("npm test", TestCommandDetector.Detect(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void PackageJsonWithOnlyBuildScriptDetectsNpmRunBuild()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "package.json"), "{\"scripts\": {\"build\": \"webpack\"}}");
            Assert.Equal("npm run build", TestCommandDetector.Detect(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void PackageJsonWithNeitherScriptDetectsNothing()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "package.json"), "{\"scripts\": {\"start\": \"node index.js\"}}");
            Assert.Null(TestCommandDetector.Detect(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void PyprojectWithTestsDirDetectsPytest()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "pyproject.toml"), "[project]\nname = \"x\"");
            Directory.CreateDirectory(Path.Combine(dir, "tests"));
            Assert.Equal("pytest", TestCommandDetector.Detect(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CsprojNamedTestsDetectsDotnetTest()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "Foo.Tests.csproj"), "<Project />");
            Assert.Equal("dotnet test", TestCommandDetector.Detect(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CsprojWithoutTestNameDetectsDotnetBuild()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "Foo.csproj"), "<Project />");
            Assert.Equal("dotnet build", TestCommandDetector.Detect(dir));
        }
        finally { Directory.Delete(dir, true); }
    }
}
