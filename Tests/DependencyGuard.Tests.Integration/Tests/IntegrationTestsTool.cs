using NUnit.Framework;

namespace DependencyGuard.Tests.Integration.Tests;

[TestFixture]
public sealed class IntegrationTestsTool : IntegrationTestsBase
{
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
    public async Task Tool_ShouldUseConfigFromFlag_WhenProjectHasItsOwnConfig()
    {
        // Arrange
        string configPath = Path.Combine(TestDirectory, "global.yaml");
        File.WriteAllText(configPath, """
            allowed:
              - from: MyApp.Application
                to: MyApp.Domain
            """);

        // The project's own config allows the dependency; the --config file does not.
        string projectDir = Path.Combine(TestDirectory, "project");
        Directory.CreateDirectory(projectDir);
        WriteCsproj("MyProject", projectDir);
        WriteYaml("""
            allowed:
              - from: MyApp.Application
                to: MyApp.Infrastructure
            """, projectDir);
        WriteSource("OrderService.cs", """
            namespace MyApp.Application;
            using MyApp.Infrastructure;
            public class OrderService { }
            """, projectDir);

        // Act
        (string output, int exitCode) = await RunToolAsync(projectDir, $"--config \"{configPath}\"");

        // Assert
        Assert.That(exitCode, Is.EqualTo(1), $"Expected exit code 1. Output:\n{output}");
        Assert.That(output, Does.Contain("DG0001"), $"Expected DG0001. Output:\n{output}");
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
        Assert.That(output, Does.Contain("failed to parse config"), $"Expected parse error. Output:\n{output}");
    }

    [Test]
    public async Task Tool_ShouldExitWithCode1_WhenSourceIsNotPermittedExposedToConsumer()
    {
        // Arrange
        WriteCsproj("MyProject");
        WriteYaml("""
            exposedTo:
              - namespace: MyApp.Infra
                consumers:
                  - MyApp.Application
            """);
        WriteSource("Source.cs", """
            namespace MyApp.UI;
            using MyApp.Infra;
            public class UiPage { }
            """);

        // Act
        (string output, int exitCode) = await RunToolAsync(TestDirectory);

        // Assert
        Assert.That(exitCode, Is.EqualTo(1), $"Expected exit code 1. Output:\n{output}");
        Assert.That(output, Does.Contain("DG0001"), $"Expected DG0001. Output:\n{output}");
    }

    [Test]
    public async Task Tool_ShouldExitWithCode0_WhenSourceIsPermittedExposedToConsumer()
    {
        // Arrange
        WriteCsproj("MyProject");
        WriteYaml("""
            exposedTo:
              - namespace: MyApp.Infra
                consumers:
                  - MyApp.Application
            """);
        WriteSource("Source.cs", """
            namespace MyApp.Application;
            using MyApp.Infra;
            public class UseCase { }
            """);

        // Act
        (string output, int exitCode) = await RunToolAsync(TestDirectory);

        // Assert
        Assert.That(exitCode, Is.EqualTo(0), $"Expected clean exit. Output:\n{output}");
        Assert.That(output, Does.Not.Contain("DG0001"));
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

    private static Task<(string Output, int ExitCode)> RunToolAsync(string targetPath, string extraArgs = "")
    {
        return RunCliAsync($"{extraArgs} \"{targetPath}\"");
    }
}
