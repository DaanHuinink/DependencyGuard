using NUnit.Framework;

namespace DependencyGuard.Tests.Integration;

// Gives every test its own temporary project directory plus helpers to fill it.
public abstract class IntegrationTestsBase
{
    protected string TestDirectory { get; private set; } = null!;

    // Extra MSBuild items for the generated test project (e.g. a PackageReference).
    protected virtual string ProjectItems => string.Empty;

    [SetUp]
    public void CreateTestDirectory()
    {
        TestDirectory = Path.Combine(Path.GetTempPath(), "DG_Int_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(TestDirectory);
    }

    [TearDown]
    public void DeleteTestDirectory()
    {
        try
        {
            Directory.Delete(TestDirectory, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    protected void WriteCsproj(string name, string? dir = null, string properties = "", string sdk = "Microsoft.NET.Sdk", string items = "")
    {
        File.WriteAllText(Path.Combine(dir ?? TestDirectory, $"{name}.csproj"), $"""
            <Project Sdk="{sdk}">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                {properties}
              </PropertyGroup>
              <ItemGroup>
                {ProjectItems}
                {items}
              </ItemGroup>
            </Project>
            """);
    }

    protected void WriteYaml(string content, string? dir = null)
    {
        File.WriteAllText(Path.Combine(dir ?? TestDirectory, "dependency-guard.yaml"), content);
    }

    protected void WriteSource(string filename, string content, string? dir = null)
    {
        File.WriteAllText(Path.Combine(dir ?? TestDirectory, filename), content);
    }

    // The CLI is built once by IntegrationSetupFixture, so every run can skip the build.
    protected static Task<(string Output, int ExitCode)> RunCliAsync(string arguments)
    {
        return IntegrationSetupFixture.RunAsync(
            "dotnet",
            $"run --no-build --project \"{IntegrationSetupFixture.ToolProjectPath}\" -- {arguments}");
    }
}
