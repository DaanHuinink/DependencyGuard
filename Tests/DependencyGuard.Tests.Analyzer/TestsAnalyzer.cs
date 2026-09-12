using System.Collections.Immutable;
using DependencyGuard.Tests.Analyzer.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;

namespace DependencyGuard.Tests.Analyzer;

// Target namespaces are declared in the test source on purpose: the analyzer skips usings it can't
// resolve, so a test using an undeclared namespace would pass regardless of the configured rules.
public sealed class TestsAnalyzer
{
    private const string AllowAppToDomain = """
        allowed:
          - from: MyApp.Application
            to: MyApp.Domain
        """;

    [Test]
    public async Task Analyzer_ShouldReportNoDiagnostics_WhenDependencyIsAllowed()
    {
        // Arrange
        const string source = """
            namespace MyApp.Application
            {
                using MyApp.Domain;
            }

            namespace MyApp.Domain
            {
                class A { }
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(source, AllowAppToDomain);

        // Assert
        Assert.That(diagnostics.Length, Is.EqualTo(0));
    }

    [Test]
    public async Task Analyzer_ShouldReportDG0001_WhenDependencyIsNotAllowed()
    {
        // Arrange
        const string source = """
            namespace MyApp.Application
            {
                using MyApp.Infrastructure;
            }

            namespace MyApp.Infrastructure
            {
                class A {}
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(source, AllowAppToDomain);

        // Assert
        Assert.That(diagnostics.Length, Is.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("DG0001"));
    }

    [Test]
    public async Task Analyzer_ShouldIncludeBothNamespacesInMessage_WhenDependencyIsNotAllowed()
    {
        // Arrange
        const string source = """
            namespace MyApp.Application
            {
                using MyApp.Infrastructure;
            }
            namespace MyApp.Infrastructure
            {
                class A { }
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(source, AllowAppToDomain);

        // Assert
        Assert.That(diagnostics.Length, Is.EqualTo(1));
        string message = diagnostics[0].GetMessage();
        Assert.That(message, Does.Contain("MyApp.Application"));
        Assert.That(message, Does.Contain("MyApp.Infrastructure"));
    }

    [Test]
    public async Task Analyzer_ShouldReportDG0000_WhenConfigIsMissing()
    {
        // Arrange
        const string source = """
            namespace MyApp.Application
            {
                using MyApp.Domain;
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(source, yamlConfig: null);

        // Assert
        Assert.That(diagnostics.Length, Is.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("DG0000"));
    }

    [Test]
    public async Task Analyzer_ShouldReportNoViolation_WhenDependencyIsAllowedByAnyOfMultipleConfigs()
    {
        // Arrange
        const string source = """
            namespace MyApp.Application
            {
                using MyApp.Shared;
            }

            namespace MyApp.Shared
            {
                class A { }
            }
            """;

        const string configA = """
            allowed:
              - from: MyApp.Application
                to: MyApp.Domain
            """;

        const string configB = """
            allowed:
              - from: MyApp.Application
                to: MyApp.Shared
            """;

        (string, string)[] additionalFiles =
        [
            ("solution/dependency-guard.yaml", configA),
            ("project/dependency-guard.yaml", configB)
        ];

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(source, additionalFiles);

        // Assert
        Assert.That(diagnostics.Length, Is.EqualTo(0));
    }

    [Test]
    public async Task Analyzer_ShouldReportDG0001_WhenDependencyIsAllowedByNoneOfMultipleConfigs()
    {
        // Arrange
        const string source = """
            namespace MyApp.Application
            {
                using MyApp.Infrastructure;
            }

            namespace MyApp.Infrastructure { }
            """;

        const string configA = """
            allowed:
              - from: MyApp.Application
                to: MyApp.Domain
            """;

        const string configB = """
            allowed:
              - from: MyApp.Application
                to: MyApp.Shared
            """;

        (string, string)[] additionalFiles =
        [
            ("solution/dependency-guard.yaml", configA),
            ("project/dependency-guard.yaml", configB)
        ];

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(source, additionalFiles);

        // Assert
        Assert.That(diagnostics.Any(d => d.Id == "DG0001"), Is.True);
    }

    [Test]
    public async Task Analyzer_ShouldTreatAsSingleConfig_WhenConfigPathsAreDuplicated()
    {
        // Arrange
        // If the duplicated path were loaded twice, the exposedTo entry would be declared twice → DG0003.
        const string yaml = """
            allowed:
              - from: MyApp.Application
                to: MyApp.Domain
            exposedTo:
              - namespace: MyApp.Domain
                consumers:
                  - MyApp.Application
            """;

        const string source = """
            namespace MyApp.Application
            {
                using MyApp.Domain;
            }

            namespace MyApp.Domain
            {
                class A { }
            }
            """;

        (string, string)[] additionalFiles =
        [
            (DependencyGuard.Analyzer.Analyzer.ConfigFileName, yaml),
            (DependencyGuard.Analyzer.Analyzer.ConfigFileName, yaml)
        ];

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(source, additionalFiles);

        // Assert
        Assert.That(diagnostics.Length, Is.EqualTo(0));
    }

    [Test]
    public async Task Analyzer_ShouldReportNoDiagnostics_WhenFileScopedNamespaceUsesAllowedDependency()
    {
        // Arrange
        const string source = """
            namespace MyApp.Application;

            using MyApp.Domain;
            """;

        const string target = """
            namespace MyApp.Domain;

            class A { }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync([source, target], AllowAppToDomain);

        // Assert
        Assert.That(diagnostics.Length, Is.EqualTo(0));
    }

    [Test]
    public async Task Analyzer_ShouldReportDG0001_WhenFileScopedNamespaceUsesDisallowedDependency()
    {
        // Arrange
        const string source = """
            namespace MyApp.Application;

            using MyApp.Infrastructure;
            """;

        const string target = """
            namespace MyApp.Infrastructure;

            class A { }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync([source, target], AllowAppToDomain);

        // Assert
        Assert.That(diagnostics.Length, Is.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("DG0001"));
    }

    [Test]
    public async Task Analyzer_ShouldReportDG0001_WhenUsingNamespaceCarvedOutByDeny()
    {
        // Arrange
        const string yaml = """
            allowed:
              - from: MyApp.Application
                to: System
            denied:
              - from: MyApp.Application
                to: System.IO
            """;

        const string source = """
            namespace MyApp.Application
            {
                using System.IO;
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(source, yaml);

        // Assert
        Assert.That(diagnostics.Length, Is.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("DG0001"));
    }

    [Test]
    public async Task Analyzer_ShouldReportNoDiagnostics_WhenUsingSiblingOfDenyCarveOut()
    {
        // Arrange
        const string yaml = """
            allowed:
              - from: MyApp.Application
                to: System.*
            denied:
              - from: MyApp.Application
                to: System.IO.*
            """;

        const string source = """
            namespace MyApp.Application
            {
                using System.Collections.Generic;
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(source, yaml);

        // Assert
        Assert.That(diagnostics.Length, Is.EqualTo(0));
    }

    [Test]
    public async Task Analyzer_ShouldIgnoreUsing_WhenUsingIsGlobal()
    {
        // Arrange
        const string source = """
            global using MyApp.Infrastructure;

            namespace MyApp.Application
            {
            }

            namespace MyApp.Infrastructure
            {
                class A { }
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(source, AllowAppToDomain);

        // Assert
        Assert.That(diagnostics.Length, Is.EqualTo(0));
    }

    [Test]
    public async Task Analyzer_ShouldIgnoreUsing_WhenUsingIsStatic()
    {
        // Arrange
        const string source = """
            namespace MyApp.Application
            {
                using static MyApp.Infrastructure.SomeClass;
            }

            namespace MyApp.Infrastructure
            {
                public static class SomeClass { }
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(source, AllowAppToDomain);

        // Assert
        Assert.That(diagnostics.Length, Is.EqualTo(0));
    }

    [Test]
    public async Task Analyzer_ShouldIgnoreUsing_WhenUsingIsAlias()
    {
        // Arrange
        const string source = """
            namespace MyApp.Application
            {
                using Infra = MyApp.Infrastructure;
            }

            namespace MyApp.Infrastructure
            {
                class A { }
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(source, AllowAppToDomain);

        // Assert
        Assert.That(diagnostics.Length, Is.EqualTo(0));
    }

    [Test]
    public async Task Analyzer_ShouldReportDG0003_WhenSamePairIsBothAllowedAndDenied()
    {
        // Arrange
        const string yaml = """
            allowed:
              - from: MyApp.Application
                to: MyApp.Domain
            denied:
              - from: MyApp.Application
                to: MyApp.Domain
            """;

        const string source = """
            namespace MyApp.Application
            {
                using MyApp.Domain;
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(source, yaml);

        // Assert
        Assert.That(diagnostics.Length, Is.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("DG0003"));
    }

    [Test]
    public async Task Analyzer_ShouldReportDG0003_WhenAllowHasMoreSpecificSourceAndDenyHasMoreSpecificTarget()
    {
        // Arrange
        const string yaml = """
            allowed:
              - from: MyApp.Application
                to: MyApp.Infrastructure
            denied:
              - from: MyApp
                to: MyApp.Infrastructure.Database
            """;

        const string source = """
            namespace MyApp.Application
            {
                using MyApp.Infrastructure;
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(source, yaml);

        // Assert
        Assert.That(diagnostics.Length, Is.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("DG0003"));
    }

    [Test]
    public async Task Analyzer_ShouldReportDG0003_WhenDenyHasMoreSpecificSourceAndAllowHasMoreSpecificTarget()
    {
        // Arrange
        const string yaml = """
            denied:
              - from: MyApp.Application
                to: MyApp.Infrastructure
            allowed:
              - from: MyApp
                to: MyApp.Infrastructure.PublicApi
            """;

        const string source = """
            namespace MyApp.Application
            {
                using MyApp.Infrastructure;
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(source, yaml);

        // Assert
        Assert.That(diagnostics.Length, Is.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("DG0003"));
    }

    [Test]
    public async Task Analyzer_ShouldReportDG0003_WhenAllowAndDenyAreEquallySpecific()
    {
        // Arrange
        // Without the conflict, the allow would silently win for `using System;`.
        const string yaml = """
            allowed:
              - from: MyApp.Application
                to: System.*
            denied:
              - from: MyApp.Application
                to: System
            """;

        const string source = """
            namespace MyApp.Application
            {
                using System;
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(source, yaml);

        // Assert
        Assert.That(diagnostics.Length, Is.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("DG0003"));
    }

    [Test]
    public async Task Analyzer_ShouldReportDG0001AtTypeReference_WhenFullyQualifiedTypeIsNotAllowed()
    {
        // Arrange
        // No `using` directive: the violation is at the fully-qualified type reference.
        const string source = """
            namespace MyApp.Domain
            {
                public class Order { }
            }
            namespace MyApp.Infrastructure
            {
                public class Repository { }
            }
            namespace MyApp.Application
            {
                public class OrderService
                {
                    MyApp.Infrastructure.Repository _repo = null!;
                }
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(source, AllowAppToDomain);

        // Assert
        Assert.That(diagnostics.Length, Is.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("DG0001"));
        TextSpan span = diagnostics[0].Location.SourceSpan;
        Assert.That(source.Substring(span.Start, span.Length), Is.EqualTo("Repository"));
    }

    [Test]
    public async Task Analyzer_ShouldReportNoDiagnostics_WhenFullyQualifiedTypeIsAllowed()
    {
        // Arrange
        const string source = """
            namespace MyApp.Domain
            {
                public class Order { }
            }
            namespace MyApp.Application
            {
                public class OrderService
                {
                    MyApp.Domain.Order _order = null!;
                }
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(source, AllowAppToDomain);

        // Assert
        Assert.That(diagnostics.Length, Is.EqualTo(0));
    }

    [Test]
    public async Task Analyzer_ShouldReportNoDiagnostics_WhenSourceIsPermittedExposedToConsumer()
    {
        // Arrange
        const string yaml = """
            exposedTo:
              - namespace: MyApp.Infrastructure
                consumers:
                  - MyApp.Application
            """;

        const string source = """
            namespace MyApp.Application
            {
                using MyApp.Infrastructure;
            }

            namespace MyApp.Infrastructure
            {
                class A { }
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(source, yaml);

        // Assert
        Assert.That(diagnostics.Length, Is.EqualTo(0));
    }

    [Test]
    public async Task Analyzer_ShouldReportDG0001_WhenSourceIsNotPermittedExposedToConsumer()
    {
        // Arrange
        const string yaml = """
            exposedTo:
              - namespace: MyApp.Infrastructure
                consumers:
                  - MyApp.Application
            """;

        const string source = """
            namespace MyApp.UI
            {
                using MyApp.Infrastructure;
            }

            namespace MyApp.Infrastructure
            {
                class A {}
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(source, yaml);

        // Assert
        Assert.That(diagnostics.Length, Is.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("DG0001"));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("only accessible to"));
    }

    [Test]
    public async Task Analyzer_ShouldReportNoDiagnostics_WhenSourceIsSubNamespaceOfWildcardConsumer()
    {
        // Arrange
        const string yaml = """
            exposedTo:
              - namespace: MyApp.Infrastructure
                consumers:
                  - MyApp.Application.*
            """;

        const string source = """
            namespace MyApp.Application.Orders
            {
                using MyApp.Infrastructure;
            }

            namespace MyApp.Infrastructure
            {
                class A { }
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(source, yaml);

        // Assert
        Assert.That(diagnostics.Length, Is.EqualTo(0));
    }

    [Test]
    public async Task Analyzer_ShouldReportDG0001_WhenTargetIsSubNamespaceOfExposedToNamespace()
    {
        // Arrange
        const string yaml = """
            exposedTo:
              - namespace: MyApp.Infrastructure
                consumers:
                  - MyApp.Application
            """;

        const string source = """
            namespace MyApp.UI
            {
                using MyApp.Infrastructure.Persistence;
            }

            namespace MyApp.Infrastructure.Persistence {}
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(source, yaml);

        // Assert
        Assert.That(diagnostics.Length, Is.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("DG0001"));
    }

    [Test]
    public async Task Analyzer_ShouldReportNoDiagnostics_WhenExplicitAllowOverridesExposedTo()
    {
        // Arrange
        const string yaml = """
            allowed:
              - from: MyApp.UI
                to: MyApp.Infrastructure
            exposedTo:
              - namespace: MyApp.Infrastructure
                consumers:
                  - MyApp.Application
            """;

        const string source = """
            namespace MyApp.UI
            {
                using MyApp.Infrastructure;
            }

            namespace MyApp.Infrastructure
            {
                class A { }
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(source, yaml);

        // Assert
        Assert.That(diagnostics.Length, Is.EqualTo(0));
    }

    [Test]
    public async Task Analyzer_ShouldReportDG0003_WhenExposedToNamespaceIsDuplicated()
    {
        // Arrange
        const string yaml = """
            exposedTo:
              - namespace: MyApp.Infrastructure
                consumers:
                  - MyApp.Application
              - namespace: MyApp.Infrastructure
                consumers:
                  - MyApp.UI
            """;

        const string source = """
            namespace MyApp.Application
            {
                using MyApp.Infrastructure;
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(source, yaml);

        // Assert
        Assert.That(diagnostics.Any(d => d.Id == "DG0003"), Is.True);
    }

    [Test]
    public async Task Analyzer_ShouldReportNoDiagnostics_WhenFromIsRootWildcard()
    {
        // Arrange
        const string yaml = """
            allowed:
            - from: .*
              to: System.*
            """;

        const string source = """
            namespace MyApp
            {
              using System.Reflection;
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(source, yaml);

        // Assert
        Assert.That(diagnostics.Length, Is.EqualTo(0));
    }
}
