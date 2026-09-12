using NUnit.Framework;

namespace DependencyGuard.Tests.Integration.Tests;

[TestFixture]
public sealed class IntegrationTestsInspector : IntegrationTestsBase
{
    private const string ExistingConfig = "# existing config\n";

    private string ConfigPath => Path.Combine(TestDirectory, "dependency-guard.yaml");

    [Test]
    public async Task Generate_ShouldWriteAllowRules_WhenProjectHasCrossNamespaceUsing()
    {
        // Arrange
        WriteCsproj("MyProject");
        WriteSource("OrderService.cs", """
            namespace MyApp.Application;
            using MyApp.Domain;
            public class OrderService { }
            """);

        // Act
        (string output, int exitCode) = await RunGenerateAsync(TestDirectory);

        // Assert
        Assert.That(exitCode, Is.EqualTo(0), $"Expected exit code 0. Output:\n{output}");

        string yaml = await File.ReadAllTextAsync(ConfigPath);
        Assert.That(yaml, Does.Contain("MyApp.Application"), $"YAML:\n{yaml}");
        Assert.That(yaml, Does.Contain("MyApp.Domain"), $"YAML:\n{yaml}");
    }

    [Test]
    public async Task Generate_ShouldWriteSingleRule_WhenSameDependencyAppearsInMultipleFiles()
    {
        // Arrange
        WriteCsproj("MyProject");
        WriteSource("Service1.cs", """
            namespace MyApp.Application;
            using MyApp.Domain;
            public class Service1 { }
            """);
        WriteSource("Service2.cs", """
            namespace MyApp.Application;
            using MyApp.Domain;
            public class Service2 { }
            """);

        // Act
        (string output, int exitCode) = await RunGenerateAsync(TestDirectory);

        // Assert
        Assert.That(exitCode, Is.EqualTo(0), $"Output:\n{output}");

        string yaml = await File.ReadAllTextAsync(ConfigPath);
        int fromCount = CountOccurrences(yaml, "from: MyApp.Application");
        Assert.That(fromCount, Is.EqualTo(1), $"Expected exactly one rule entry. YAML:\n{yaml}");
    }

    [Test]
    public async Task Generate_ShouldWriteAllowRule_WhenProjectHasFullyQualifiedTypeReference()
    {
        // Arrange
        WriteCsproj("MyProject");
        WriteSource("Service.cs", """
            namespace MyApp.Application
            {
                public class Service
                {
                    MyApp.Domain.Entity _e = null!;
                }
            }
            """);

        // Act
        (string output, int exitCode) = await RunGenerateAsync(TestDirectory);

        // Assert
        Assert.That(exitCode, Is.EqualTo(0), $"Output:\n{output}");

        string yaml = await File.ReadAllTextAsync(ConfigPath);
        Assert.That(yaml, Does.Contain("from: MyApp.Application"), $"YAML:\n{yaml}");
        Assert.That(yaml, Does.Contain("to: MyApp.Domain"), $"YAML:\n{yaml}");
    }

    [Test]
    public async Task Generate_ShouldFailAndKeepConfig_WhenConfigExistsAndForceIsNotPassed()
    {
        // Arrange
        WriteCsproj("MyProject");
        WriteSource("Service.cs", """
            namespace MyApp.Application;
            using MyApp.Domain;
            public class Service { }
            """);
        WriteYaml(ExistingConfig);

        // Act
        (string output, int exitCode) = await RunGenerateAsync(TestDirectory);

        // Assert
        Assert.That(exitCode, Is.Not.EqualTo(0), $"Expected failure. Output:\n{output}");
        Assert.That(output, Does.Contain("already exists").IgnoreCase, $"Output:\n{output}");
        Assert.That(await File.ReadAllTextAsync(ConfigPath), Is.EqualTo(ExistingConfig), "Existing config should be left untouched.");
    }

    [Test]
    public async Task Generate_ShouldOverwriteConfig_WhenConfigExistsAndForceIsPassed()
    {
        // Arrange
        WriteCsproj("MyProject");
        WriteSource("Service.cs", """
            namespace MyApp.Application;
            using MyApp.Domain;
            public class Service { }
            """);
        WriteYaml(ExistingConfig);

        // Act
        (string output, int exitCode) = await RunGenerateAsync(TestDirectory, "--force");

        // Assert
        Assert.That(exitCode, Is.EqualTo(0), $"Expected success with --force. Output:\n{output}");

        string yaml = await File.ReadAllTextAsync(ConfigPath);
        Assert.That(yaml, Does.Not.Contain("# existing config"), $"YAML:\n{yaml}");
        Assert.That(yaml, Does.Contain("MyApp.Domain"), $"YAML:\n{yaml}");
    }

    [Test]
    public async Task Generate_ShouldWriteNoRules_WhenProjectHasNoCrossNamespaceDependencies()
    {
        // Arrange
        WriteCsproj("MyProject");
        WriteSource("Service.cs", """
            namespace MyApp.Application;
            public class Service { }
            """);

        // Act
        (string output, int exitCode) = await RunGenerateAsync(TestDirectory);

        // Assert
        Assert.That(exitCode, Is.EqualTo(0), $"Output:\n{output}");

        string yaml = await File.ReadAllTextAsync(ConfigPath);
        // Empty project has no cross-namespace dependencies, so no 'from:' entries
        Assert.That(yaml, Does.Not.Contain("from:"), $"Expected no rules. YAML:\n{yaml}");
    }

    private static Task<(string Output, int ExitCode)> RunGenerateAsync(string targetPath, string extraArgs = "")
    {
        return RunCliAsync($"generate {extraArgs} \"{targetPath}\"");
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
