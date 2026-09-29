using NUnit.Framework;

namespace DependencyGuard.Tests.Integration.Tests;

[TestFixture]
public sealed class IntegrationTestsTool : IntegrationTestsBase
{
    [SetUp]
    public void WriteNamespaces()
    {
        WriteReferencedNamespaces();
    }

    [Test]
    public async Task Tool_ShouldExitWithCode1AndReportDG0001_WhenDependencyIsNotAllowed()
    {
        // Arrange
        WriteCsproj("MyProject");
        WriteYaml("""
            allowed:
              - from: MyApp.Application
                to: MyApp.Domain
            """);
        WriteSource("OrderService.cs", """
            namespace MyApp.Application;
            using MyApp.Infrastructure;
            public class OrderService { }
            """);

        // Act
        (string output, int exitCode) = await RunToolAsync(TestDirectory);

        // Assert
        Assert.That(exitCode, Is.EqualTo(1), $"Expected exit code 1. Output:\n{output}");
        Assert.That(output, Does.Contain("DG0001"), $"Expected DG0001 in output:\n{output}");
    }

    [Test]
    public async Task Tool_ShouldExitWithCode0_WhenDependencyIsAllowed()
    {
        // Arrange
        WriteCsproj("MyProject");
        WriteYaml("""
            allowed:
              - from: MyApp.Application
                to: MyApp.Domain
            """);
        WriteSource("OrderService.cs", """
            namespace MyApp.Application;
            using MyApp.Domain;
            public class OrderService { }
            """);

        // Act
        (string output, int exitCode) = await RunToolAsync(TestDirectory);

        // Assert
        Assert.That(exitCode, Is.EqualTo(0), $"Expected clean exit. Output:\n{output}");
        Assert.That(output, Does.Not.Contain("DG0001"));
    }

    [Test]
    public async Task Tool_ShouldExitWithCode1AndReportDG0001_WhenFullyQualifiedTypeIsNotAllowed()
    {
        // Arrange
        WriteCsproj("MyProject");
        WriteYaml("""
            allowed:
              - from: MyApp.Application
                to: MyApp.Domain
            """);
        WriteSource("OrderService.cs", """
            namespace MyApp.Application;
            public class OrderService
            {
                MyApp.Infrastructure.Repository _repo = null!;
            }
            """);

        // Act
        (string output, int exitCode) = await RunToolAsync(TestDirectory);

        // Assert
        Assert.That(exitCode, Is.EqualTo(1), $"Expected exit code 1. Output:\n{output}");
        Assert.That(output, Does.Contain("DG0001"), $"Expected DG0001 in output:\n{output}");
    }

    [Test]
    public async Task Tool_ShouldMergeConfigFromFlagWithProjectConfig_WhenProjectHasItsOwnConfig()
    {
        // Arrange
        string configPath = Path.Combine(TestDirectory, "global.yaml");
        File.WriteAllText(configPath, """
            allowed:
              - from: MyApp.Application
                to: MyApp.Domain
            """);

        // The project's own config allows the dependency. The --config file adds its rules to it.
        string projectDir = Path.Combine(TestDirectory, "project");
        Directory.CreateDirectory(projectDir);
        WriteCsproj("MyProject", projectDir);
        WriteYaml("""
            allowed:
              - from: MyApp.Application
                to: MyApp.Infrastructure
            """, projectDir);
        WriteReferencedNamespaces(projectDir);
        WriteSource("OrderService.cs", """
            namespace MyApp.Application;
            using MyApp.Infrastructure;
            public class OrderService { }
            """, projectDir);

        // Act
        (string output, int exitCode) = await RunToolAsync(projectDir, $"--config \"{configPath}\"");

        // Assert
        Assert.That(exitCode, Is.EqualTo(0), $"Expected clean exit. Output:\n{output}");
        Assert.That(output, Does.Not.Contain("DG0001"), $"Output:\n{output}");
    }

    [Test]
    public async Task Tool_ShouldSkipProjectAndExitWithCode0_WhenConfigIsMissing()
    {
        // Arrange
        WriteCsproj("MyProject");
        WriteSource("OrderService.cs", """
            namespace MyApp.Application;
            using MyApp.Infrastructure;
            public class OrderService { }
            """);

        // Act
        (string output, int exitCode) = await RunToolAsync(TestDirectory);

        // Assert
        Assert.That(exitCode, Is.EqualTo(0), $"Output:\n{output}");
        Assert.That(output, Does.Contain("skip"), $"Expected skip message. Output:\n{output}");
    }

    [Test]
    public async Task Tool_ShouldExitWithCode1_WhenConfigHasConflictingRules()
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
        WriteSource("Service.cs", """
            namespace MyApp.Application;
            public class Service { }
            """);

        // Act
        (string output, int exitCode) = await RunToolAsync(TestDirectory);

        // Assert
        Assert.That(exitCode, Is.EqualTo(1), $"Expected exit code 1. Output:\n{output}");
        Assert.That(output, Does.Contain("both allowed and denied"), $"Expected conflict message. Output:\n{output}");
    }

    [Test]
    public async Task Tool_ShouldExitWithCode1_WhenConfigIsMalformed()
    {
        // Arrange
        WriteCsproj("MyProject");
        WriteYaml("""
            allowed: [
            """);
        WriteSource("Service.cs", """
            namespace MyApp.Application;
            public class Service { }
            """);

        // Act
        (string output, int exitCode) = await RunToolAsync(TestDirectory);

        // Assert
        Assert.That(exitCode, Is.EqualTo(1), $"Expected exit code 1. Output:\n{output}");
        Assert.That(output, Does.Contain("error DG0004"), $"Expected a DG0004 for the rule file. Output:\n{output}");
    }

    [Test]
    public async Task Tool_ShouldExitWithCode1_WhenChildSourceNamespaceViolatesRules()
    {
        // Arrange
        WriteCsproj("MyProject");
        WriteYaml("""
            allowed:
              - from: MyApp.Application
                to: MyApp.Domain
            """);
        WriteSource("Source.cs", """
            namespace MyApp.Application.Orders;
            using MyApp.Infrastructure;
            public class OrderUseCase { }
            """);

        // Act
        (string output, int exitCode) = await RunToolAsync(TestDirectory);

        // Assert
        Assert.That(exitCode, Is.EqualTo(1), $"Expected exit code 1. Output:\n{output}");
        Assert.That(output, Does.Contain("DG0001"), $"Expected DG0001. Output:\n{output}");
    }

    [Test]
    public async Task Tool_ShouldExitWithCode0_WhenChildSourceNamespaceIsCoveredByWildcardRule()
    {
        // Arrange
        WriteCsproj("MyProject");
        // `from: MyApp.Application.*` covers Application and all sub-namespaces
        WriteYaml("""
            allowed:
              - from: MyApp.Application.*
                to: MyApp.Domain.*
            """);
        WriteSource("Source.cs", """
            namespace MyApp.Application.Orders;
            using MyApp.Domain;
            public class OrderUseCase { }
            """);

        // Act
        (string output, int exitCode) = await RunToolAsync(TestDirectory);

        // Assert
        Assert.That(exitCode, Is.EqualTo(0), $"Expected clean exit. Output:\n{output}");
        Assert.That(output, Does.Not.Contain("DG0001"));
    }

    [Test]
    public async Task Tool_ShouldExitWithCode1_WhenUsingNamespaceCarvedOutByDeny()
    {
        // Arrange
        WriteCsproj("MyProject");
        WriteYaml("""
            allowed:
              - from: MyApp.Application
                to: System.*
            denied:
              - from: MyApp.Application
                to: System.IO.*
            """);
        WriteSource("Source.cs", """
            namespace MyApp.Application;
            using System.IO;
            public class Service { }
            """);

        // Act
        (string output, int exitCode) = await RunToolAsync(TestDirectory);

        // Assert
        Assert.That(exitCode, Is.EqualTo(1), $"Expected exit code 1. Output:\n{output}");
        Assert.That(output, Does.Contain("DG0001"), $"Expected DG0001. Output:\n{output}");
    }

    [Test]
    public async Task Tool_ShouldShowUsageAndExitWithCode0_WhenHelpIsAsked()
    {
        // Act
        (string output, int exitCode) = await RunCliAsync("--help");

        // Assert
        Assert.That(exitCode, Is.EqualTo(0), $"Output:\n{output}");
        Assert.That(output, Does.Contain("Usage:"), $"Output:\n{output}");
    }

    [Test]
    public async Task Tool_ShouldShowTheVersion_WhenVersionIsAsked()
    {
        // Act
        (string output, int exitCode) = await RunCliAsync("--version");

        // Assert
        Assert.That(exitCode, Is.EqualTo(0), $"Output:\n{output}");
        Assert.That(output.Trim(), Does.Match(@"^\d+\.\d+\.\d+"), $"Output:\n{output}");
    }

    [Test]
    public async Task Tool_ShouldExitWithCode2_WhenOptionIsUnknown()
    {
        // Arrange
        WriteCsproj("MyProject");

        // Act
        (string output, int exitCode) = await RunToolAsync(TestDirectory, "--confg rules.yaml");

        // Assert
        Assert.That(exitCode, Is.EqualTo(2), $"Output:\n{output}");
        Assert.That(output, Does.Contain("Unknown option: --confg"), $"Output:\n{output}");
    }

    [Test]
    public async Task Tool_ShouldMergeEveryConfigFromFlags_WhenFlagIsRepeated()
    {
        // Arrange
        string domainRules = Path.Combine(TestDirectory, "domain.yaml");
        File.WriteAllText(domainRules, """
            allowed:
              - from: MyApp.Application
                to: MyApp.Domain
            """);
        string infrastructureRules = Path.Combine(TestDirectory, "infrastructure.yaml");
        File.WriteAllText(infrastructureRules, """
            allowed:
              - from: MyApp.Application
                to: MyApp.Infrastructure
            """);
        WriteCsproj("MyProject");
        WriteSource("Service.cs", """
            namespace MyApp.Application;
            using MyApp.Domain;
            using MyApp.Infrastructure;
            public class Service { }
            """);

        // Act
        (string output, int exitCode) = await RunToolAsync(TestDirectory, $"-c \"{domainRules}\" --config \"{infrastructureRules}\"");

        // Assert
        Assert.That(exitCode, Is.EqualTo(0), $"Output:\n{output}");
    }

    [Test]
    public async Task Tool_ShouldUseTheProjectName_WhenFileHasNoNamespace()
    {
        // Arrange
        WriteCsproj("MyProject", properties: "<OutputType>Exe</OutputType>");
        WriteYaml("""
            allowed:
              - from: MyProject
                to: MyApp.Domain
            """);
        WriteSource("Program.cs", """
            using MyApp.Domain;
            using MyApp.Infrastructure;
            """);

        // Act
        (string output, int exitCode) = await RunToolAsync(TestDirectory);

        // Assert
        Assert.That(exitCode, Is.EqualTo(1), $"Output:\n{output}");
        Assert.That(output, Does.Contain("No rule allows 'MyProject' to depend on 'MyApp.Infrastructure'"), $"Output:\n{output}");
        Assert.That(output, Does.Not.Contain("to depend on 'MyApp.Domain'"), $"Output:\n{output}");
    }

    [Test]
    public async Task Tool_ShouldReportTheLine_WhenRuleFileHasATypo()
    {
        // Arrange
        WriteCsproj("MyProject");
        WriteYaml("""
            denied:
              - from: MyApp.Application
                too: System.IO
            """);
        WriteSource("Service.cs", """
            namespace MyApp.Application;
            public class Service { }
            """);

        // Act
        (string output, int exitCode) = await RunToolAsync(TestDirectory);

        // Assert
        Assert.That(exitCode, Is.EqualTo(1), $"Output:\n{output}");
        Assert.That(output, Does.Contain("dependency-guard.yaml(3,5): error DG0004: Unknown key 'too'"), $"Output:\n{output}");
    }

    [Test]
    public async Task Tool_ShouldReportTheUseInTheRazorFile_WhenComponentUsesDisallowedNamespace()
    {
        // Arrange
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
        (string output, int exitCode) = await RunToolAsync(TestDirectory);

        // Assert
        Assert.That(exitCode, Is.EqualTo(1), $"Output:\n{output}");
        Assert.That(output, Does.Contain("Page.razor(2,"), $"Expected the use in Page.razor. Output:\n{output}");
        Assert.That(output, Does.Contain("'MyProject.Components' to depend on 'MyApp.Infrastructure'"), $"Output:\n{output}");
    }

    [Test]
    public async Task Tool_ShouldReportDG0001_WhenTypeComesFromImplicitUsings()
    {
        // Arrange
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
        (string output, int exitCode) = await RunToolAsync(TestDirectory);

        // Assert
        Assert.That(exitCode, Is.EqualTo(1), $"Output:\n{output}");
        Assert.That(output, Does.Contain("No rule allows 'MyApp.Application' to depend on 'System.IO'"), $"Output:\n{output}");
    }

    [Test]
    public async Task Tool_ShouldCheckALinkedFile_WhenProjectCompilesASourceFromElsewhere()
    {
        // Arrange
        string projectDir = Path.Combine(TestDirectory, "project");
        string sharedDir = Path.Combine(TestDirectory, "shared");
        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(sharedDir);
        WriteCsproj("MyProject", projectDir, items: """<Compile Include="..\shared\Clock.cs" Link="shared\Clock.cs" />""");
        WriteReferencedNamespaces(projectDir);
        WriteYaml("""
            allowed:
              - from: MyApp.Application
                to: MyApp.Domain
            """, projectDir);
        WriteSource("Clock.cs", """
            namespace MyApp.Application;
            public class Clock { private MyApp.Infrastructure.Repository? _repository; }
            """, sharedDir);

        // Act
        (string output, int exitCode) = await RunToolAsync(projectDir);

        // Assert
        Assert.That(exitCode, Is.EqualTo(1), $"Output:\n{output}");
        Assert.That(output, Does.Contain("Clock.cs(2,"), $"Output:\n{output}");
    }

    [Test]
    public async Task Tool_ShouldUseTheRuleFilesOfTheBuild_WhenMSBuildGivesThemToTheProject()
    {
        // Arrange
        WriteNuGetConfig();
        string rules = Path.Combine(TestDirectory, "rules");
        string projectDir = Path.Combine(TestDirectory, "project");
        Directory.CreateDirectory(rules);
        Directory.CreateDirectory(projectDir);
        WriteYaml("""
            allowed:
              - from: MyApp.Application
                to: MyApp.Domain
            """, rules);
        File.WriteAllText(Path.Combine(TestDirectory, "Directory.Build.props"), """
            <Project>
              <ItemGroup>
                <DependencyGuardConfig Include="$(MSBuildThisFileDirectory)rules\dependency-guard.yaml" />
              </ItemGroup>
            </Project>
            """);
        string version = IntegrationSetupFixture.AnalyzerPackageVersion;
        string analyzerPackage = $"""<PackageReference Include="DependencyGuard.Analyzer" Version="{version}" />""";
        WriteCsproj("MyProject", projectDir, items: analyzerPackage);
        WriteReferencedNamespaces(projectDir);
        WriteSource("Service.cs", """
            namespace MyApp.Application;
            using MyApp.Infrastructure;
            public class Service { }
            """, projectDir);

        // Act
        (string output, int exitCode) = await RunToolAsync(projectDir);

        // Assert
        Assert.That(exitCode, Is.EqualTo(1), $"Output:\n{output}");
        Assert.That(output, Does.Contain("No rule allows 'MyApp.Application' to depend on 'MyApp.Infrastructure'"), $"Output:\n{output}");
    }

    private static Task<(string Output, int ExitCode)> RunToolAsync(string targetPath, string extraArgs = "")
    {
        return RunCliAsync($"{extraArgs} \"{targetPath}\"");
    }
}
