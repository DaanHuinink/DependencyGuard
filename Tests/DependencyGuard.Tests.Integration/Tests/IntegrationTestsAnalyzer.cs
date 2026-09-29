using NUnit.Framework;

namespace DependencyGuard.Tests.Integration.Tests;

[TestFixture]
public sealed class IntegrationTestsAnalyzer : IntegrationTestsBase
{
    protected override string ProjectItems =>
        $"""<PackageReference Include="DependencyGuard.Analyzer" Version="{IntegrationSetupFixture.AnalyzerPackageVersion}" />""";

    [SetUp]
    public void WriteNuGetConfig()
    {
        File.WriteAllText(Path.Combine(TestDirectory, "NuGet.Config"), $"""
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

    [Test]
    public async Task Build_ShouldSucceedWithDG0001Warning_WhenDependencyIsNotAllowed()
    {
        // Arrange
        WriteCsproj("MyProject");
        WriteYaml("""
            allowed:
              - from: MyApp.Application
                to: MyApp.Domain
            """);
        WriteSource("Namespaces.cs", """
            namespace MyApp.Infrastructure { }
            """);
        WriteSource("OrderService.cs", """
            namespace MyApp.Application
            {
                using MyApp.Infrastructure;
                public class OrderService { }
            }
            """);

        // Act
        (string output, int exitCode) = await BuildAsync();

        // Assert
        Assert.That(exitCode, Is.EqualTo(0), $"Build should succeed (DG0001 is a warning). Output:\n{output}");
        Assert.That(output, Does.Contain("DG0001"), $"Expected DG0001 in output:\n{output}");
    }

    [Test]
    public async Task Build_ShouldSucceedWithoutDG0001_WhenDependencyIsAllowed()
    {
        // Arrange
        WriteCsproj("MyProject");
        WriteYaml("""
            allowed:
              - from: MyApp.Application
                to: MyApp.Domain
            """);
        WriteSource("Namespaces.cs", """
            namespace MyApp.Domain { }
            """);
        WriteSource("OrderService.cs", """
            namespace MyApp.Application
            {
                using MyApp.Domain;
                public class OrderService { }
            }
            """);

        // Act
        (string output, int exitCode) = await BuildAsync();

        // Assert
        Assert.That(exitCode, Is.EqualTo(0), $"Output:\n{output}");
        Assert.That(output, Does.Not.Contain("DG0001"), $"Unexpected DG0001 in output:\n{output}");
    }

    [Test]
    public async Task Build_ShouldSucceedWithDG0000Warning_WhenConfigIsMissing()
    {
        // Arrange
        WriteCsproj("MyProject");
        WriteSource("OrderService.cs", """
            namespace MyApp.Application
            {
                public class OrderService { }
            }
            """);

        // Act
        (string output, int exitCode) = await BuildAsync();

        // Assert
        Assert.That(exitCode, Is.EqualTo(0), $"Output:\n{output}");
        Assert.That(output, Does.Contain("DG0000"), $"Expected DG0000 in output:\n{output}");
    }

    [Test]
    public async Task Build_ShouldReportDG0001ForEachViolation_WhenMultipleDependenciesAreNotAllowed()
    {
        // Arrange
        WriteCsproj("MyProject");
        WriteYaml("""
            allowed:
              - from: MyApp.Application
                to: MyApp.Domain
            """);
        WriteSource("Namespaces.cs", """
            namespace MyApp.Infrastructure { }
            namespace MyApp.Data { }
            """);
        WriteSource("OrderService.cs", """
            namespace MyApp.Application
            {
                using MyApp.Infrastructure;
                using MyApp.Data;
                public class OrderService { }
            }
            """);

        // Act
        (string output, int exitCode) = await BuildAsync();

        // Assert
        Assert.That(exitCode, Is.EqualTo(0), $"Output:\n{output}");
        // dotnet build prints warnings twice (during build + in summary), so deduplicate before counting.
        int dg0001Violations = output.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Contains("DG0001") && l.Contains("warning"))
            .Distinct()
            .Count();
        Assert.That(dg0001Violations, Is.EqualTo(2), $"Expected 2 DG0001 warning lines. Output:\n{output}");
    }

    [Test]
    public async Task Build_ShouldReportDG0001_WhenUsingNamespaceCarvedOutByDeny()
    {
        // Arrange
        WriteCsproj("MyProject");
        WriteYaml("""
            allowed:
              - from: .*
                to: System.*
            denied:
              - from: MyApp.Application
                to: System.IO.*
            """);
        WriteSource("Service.cs", """
            namespace MyApp.Application
            {
                using System.IO;
                public class Service { }
            }
            """);

        // Act
        (string output, int exitCode) = await BuildAsync();

        // Assert
        Assert.That(exitCode, Is.EqualTo(0), $"Output:\n{output}");
        Assert.That(output, Does.Contain("DG0001"), $"Expected DG0001. Output:\n{output}");
    }

    [Test]
    public async Task Build_ShouldNotReportDG0001_WhenUsingSiblingOfDenyCarveOut()
    {
        // Arrange
        WriteCsproj("MyProject");
        WriteYaml("""
            allowed:
              - from: .*
                to: System.*
            denied:
              - from: MyApp.Application
                to: System.IO.*
            """);
        WriteSource("Service.cs", """
            namespace MyApp.Application
            {
                using System.Linq;
                public class Service { }
            }
            """);

        // Act
        (string output, int exitCode) = await BuildAsync();

        // Assert
        Assert.That(exitCode, Is.EqualTo(0), $"Output:\n{output}");
        Assert.That(output, Does.Not.Contain("DG0001"), $"Unexpected DG0001. Output:\n{output}");
    }

    [Test]
    public async Task Build_ShouldFailWithDG0003_WhenSamePairIsBothAllowedAndDenied()
    {
        // Arrange
        WriteCsproj("MyProject");
        WriteYaml("""
            allowed:
              - from: MyApp.Application
                to: MyApp.Domain
            denied:
              - from: MyApp.Application
                to: MyApp.Domain
            """);
        // DG0003 is triggered by the YAML conflict alone; source just needs to exist.
        WriteSource("Service.cs", """
            namespace MyApp.Application
            {
                public class Service { }
            }
            """);

        // Act
        (string output, int exitCode) = await BuildAsync();

        // Assert
        Assert.That(exitCode, Is.Not.EqualTo(0), $"Expected build failure. Output:\n{output}");
        Assert.That(output, Does.Contain("DG0003"), $"Expected DG0003. Output:\n{output}");
    }

    [Test]
    public async Task Build_ShouldReportDG0001_WhenFileScopedNamespaceUsesDisallowedDependency()
    {
        // Arrange
        WriteCsproj("MyProject");
        WriteYaml("""
            allowed:
              - from: MyApp.Application
                to: MyApp.Domain
            """);
        WriteSource("Namespaces.cs", """
            namespace MyApp.Infrastructure;
            public class Repository { }
            """);
        WriteSource("Service.cs", """
            namespace MyApp.Application;
            using MyApp.Infrastructure;
            public class Service { }
            """);

        // Act
        (string output, int exitCode) = await BuildAsync();

        // Assert
        Assert.That(exitCode, Is.EqualTo(0), $"Output:\n{output}");
        Assert.That(output, Does.Contain("DG0001"), $"Expected DG0001. Output:\n{output}");
    }

    [Test]
    public async Task Build_ShouldReportDG0001_WhenTypeComesFromImplicitUsings()
    {
        // Arrange: File comes from System.IO through ImplicitUsings, without a using line in the file
        WriteCsproj("MyProject", properties: "<ImplicitUsings>enable</ImplicitUsings>");
        WriteYaml("""
            allowed:
              - from: MyApp.Application
                to: System
            """);
        WriteSource("Reader.cs", """
            namespace MyApp.Application;
            public class Reader
            {
                public string Read(string path) { return File.ReadAllText(path); }
            }
            """);

        // Act
        (string output, int exitCode) = await BuildAsync();

        // Assert
        Assert.That(exitCode, Is.EqualTo(0), $"Output:\n{output}");
        Assert.That(output, Does.Contain("No rule allows 'MyApp.Application' to depend on 'System.IO'"), $"Output:\n{output}");
    }

    [Test]
    public async Task Build_ShouldReportDG0001InTheRazorFile_WhenComponentUsesDisallowedNamespace()
    {
        // Arrange: the component's namespace is the root namespace plus its folder
        WriteCsproj("MyProject", sdk: "Microsoft.NET.Sdk.Razor", items: """<FrameworkReference Include="Microsoft.AspNetCore.App" />""");
        WriteYaml("""
            allowed:
              - from: .*
                to: System.*
              - from: .*
                to: Microsoft.*
            """);
        WriteSource("Clock.cs", """
            namespace MyApp.Infrastructure;
            public static class Clock { public static string Now() { return "now"; } }
            """);
        Directory.CreateDirectory(Path.Combine(TestDirectory, "Components"));
        WriteSource(Path.Combine("Components", "Page.razor"), """
            @using MyApp.Infrastructure
            <p>@Clock.Now()</p>
            """);

        // Act
        (string output, int exitCode) = await BuildAsync();

        // Assert
        Assert.That(exitCode, Is.EqualTo(0), $"Output:\n{output}");
        Assert.That(output, Does.Contain("Page.razor(1,"), $"Expected the using in Page.razor. Output:\n{output}");
        Assert.That(output, Does.Contain("Page.razor(2,"), $"Expected the use in Page.razor. Output:\n{output}");
        Assert.That(output, Does.Contain("'MyProject.Components' to depend on 'MyApp.Infrastructure'"), $"Output:\n{output}");
    }

    [Test]
    public async Task Build_ShouldUseTheRootNamespace_WhenCodeHasNoNamespace()
    {
        // Arrange: top-level statements belong to the project's root namespace (the project name by default)
        WriteCsproj("MyProject", properties: "<OutputType>Exe</OutputType>");
        WriteYaml("""
            allowed:
              - from: MyProject
                to: MyApp.Domain
              - from: MyProject
                to: System
            """);
        WriteSource("Domain.cs", """
            namespace MyApp.Domain { public class Order { } }
            namespace MyApp.Infrastructure { public class Repository { } }
            """);
        WriteSource("Program.cs", """
            using MyApp.Domain;
            using MyApp.Infrastructure;

            System.Console.WriteLine(new Order());
            """);

        // Act
        (string output, int exitCode) = await BuildAsync();

        // Assert
        Assert.That(exitCode, Is.EqualTo(0), $"Output:\n{output}");
        Assert.That(output, Does.Contain("No rule allows 'MyProject' to depend on 'MyApp.Infrastructure'"), $"Output:\n{output}");
        Assert.That(output, Does.Not.Contain("to depend on 'MyApp.Domain'"), $"Output:\n{output}");
    }

    [Test]
    public async Task Build_ShouldFailWithDG0004AtTheLine_WhenRuleFileHasATypo()
    {
        // Arrange
        WriteCsproj("MyProject");
        WriteYaml("""
            allowed:
              - form: MyApp.Application
                to: MyApp.Domain
            """);
        WriteSource("Service.cs", """
            namespace MyApp.Application;
            public class Service { }
            """);

        // Act
        (string output, int exitCode) = await BuildAsync();

        // Assert
        Assert.That(exitCode, Is.Not.EqualTo(0), $"Expected build failure. Output:\n{output}");
        Assert.That(output, Does.Contain("dependency-guard.yaml(2,5): error DG0004: Unknown key 'form'"), $"Output:\n{output}");
    }

    private Task<(string Output, int ExitCode)> BuildAsync()
    {
        return IntegrationSetupFixture.RunAsync("dotnet", "build", TestDirectory);
    }
}
