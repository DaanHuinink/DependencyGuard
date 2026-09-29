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

    // Restores from the analyzer package the fixture packed, into a folder of the test run's own (not the machine's
    // NuGet cache).
    protected void WriteNuGetConfig(string? dir = null)
    {
        File.WriteAllText(Path.Combine(dir ?? TestDirectory, "NuGet.Config"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config>
                <add key="globalPackagesFolder" value="{Path.Combine(IntegrationSetupFixture.LocalPackagesDir, "packages")}" />
              </config>
              <packageSources>
                <add key="local" value="{IntegrationSetupFixture.LocalPackagesDir}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """);
    }

    // The namespaces the tests' sources refer to. The CLI compiles the projects like the build, and a name that does
    // not resolve is not checked.
    protected void WriteReferencedNamespaces(string? dir = null)
    {
        WriteSource("ReferencedNamespaces.cs", """
            namespace MyApp.Domain { public class Order { } }
            namespace MyApp.Infrastructure { public class Repository { } }
            """, dir);
    }

    // The CLI is built once by IntegrationSetupFixture; every test starts the built assembly.
    protected static Task<(string Output, int ExitCode)> RunCliAsync(string arguments)
    {
        return IntegrationSetupFixture.RunAsync(
            "dotnet",
            $"\"{IntegrationSetupFixture.ToolAssemblyPath}\" {arguments}");
    }
}
