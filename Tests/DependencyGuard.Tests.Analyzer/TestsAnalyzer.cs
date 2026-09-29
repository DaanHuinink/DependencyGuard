using DependencyGuard.Roslyn.Interfaces;
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
        // An equally specific allow and deny are one conflict; loaded twice, they would be four.
        const string yaml = """
            allowed:
              - from: MyApp.Application
                to: MyApp.Domain
            denied:
              - from: MyApp.Application
                to: MyApp.Domain.*
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
            (RoslynAnalyzer.ConfigFileName, yaml),
            (RoslynAnalyzer.ConfigFileName, yaml)
        ];

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(source, additionalFiles);

        // Assert
        Assert.That(diagnostics.Length, Is.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("DG0003"));
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
                to: MyApp.Infrastructure.*
            denied:
              - from: MyApp.*
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
                to: MyApp.Infrastructure.*
            allowed:
              - from: MyApp.*
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

    private const string Infrastructure = """
        namespace MyApp.Infrastructure
        {
            public class Repository { }
            public class Outer { public class Inner { } }
            public static class Formatter { public static string Format() { return ""; } }
            public static class TextExtensions { public static string Shout(this string text) { return text; } }
        }
        """;

    [Test]
    public async Task Analyzer_ShouldReportDG0001AtTheUse_WhenTypeComesFromGlobalUsing()
    {
        // Arrange: ImplicitUsings and <Using> items are global usings too
        const string source = """
            global using MyApp.Infrastructure;

            namespace MyApp.Application
            {
                public class Service
                {
                    public Repository? Repository { get; set; }
                }
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync([source, Infrastructure], AllowAppToDomain);

        // Assert
        Assert.That(diagnostics.Select(d => d.Id).ToArray(), Is.EqualTo(new[] { "DG0001" }));
        Assert.That(SourceAt(diagnostics[0], source), Is.EqualTo("Repository"));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("'MyApp.Infrastructure'"));
    }

    [Test]
    public async Task Analyzer_ShouldReportDG0001AtTheUse_WhenTypeComesFromAlias()
    {
        // Arrange
        const string source = """
            namespace MyApp.Application
            {
                using Store = MyApp.Infrastructure.Repository;

                public class Service
                {
                    private Store? _store;
                }
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync([source, Infrastructure], AllowAppToDomain);

        // Assert
        Assert.That(diagnostics.Select(d => d.Id).ToArray(), Is.EqualTo(new[] { "DG0001" }));
        Assert.That(SourceAt(diagnostics[0], source), Is.EqualTo("Store"));
    }

    [Test]
    public async Task Analyzer_ShouldReportDG0001AtTheUse_WhenMemberComesFromStaticUsing()
    {
        // Arrange
        const string source = """
            namespace MyApp.Application
            {
                using static MyApp.Infrastructure.Formatter;

                public class Service
                {
                    public string Name { get; } = Format();
                }
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync([source, Infrastructure], AllowAppToDomain);

        // Assert
        Assert.That(diagnostics.Select(d => d.Id).ToArray(), Is.EqualTo(new[] { "DG0001" }));
        Assert.That(SourceAt(diagnostics[0], source), Is.EqualTo("Format"));
    }

    [Test]
    public async Task Analyzer_ShouldReportDG0001AtTheCall_WhenExtensionMethodComesFromAnotherNamespace()
    {
        // Arrange
        const string source = """
            global using MyApp.Infrastructure;

            namespace MyApp.Application
            {
                public class Service
                {
                    public string Greet() { return "hello".Shout(); }
                }
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync([source, Infrastructure], AllowAppToDomain);

        // Assert
        Assert.That(diagnostics.Select(d => d.Id).ToArray(), Is.EqualTo(new[] { "DG0001" }));
        Assert.That(SourceAt(diagnostics[0], source), Is.EqualTo("Shout"));
    }

    [Test]
    public async Task Analyzer_ShouldReportDG0001_WhenTypeComesFromEnclosingNamespace()
    {
        // Arrange: a child namespace sees its parent's types without a using
        const string yaml = """
            allowed:
              - from: MyApp.Application.Orders
                to: MyApp.Domain
            """;

        const string source = """
            namespace MyApp.Application
            {
                public class Service { }
            }

            namespace MyApp.Application.Orders
            {
                public class OrderService
                {
                    private Service? _service;
                }
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(source, yaml);

        // Assert
        Assert.That(diagnostics.Select(d => d.Id).ToArray(), Is.EqualTo(new[] { "DG0001" }));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("'MyApp.Application.Orders' to depend on 'MyApp.Application'"));
    }

    [Test]
    public async Task Analyzer_ShouldReportOnce_WhenNestedTypeOrStaticMemberIsUsedThroughItsType()
    {
        // Arrange: in Outer.Inner and Formatter.Format() the dependency is the name on the left
        const string source = """
            global using MyApp.Infrastructure;

            namespace MyApp.Application
            {
                public class Service
                {
                    private Outer.Inner? _inner;
                    public string Name { get; } = Formatter.Format();
                }
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync([source, Infrastructure], AllowAppToDomain);

        // Assert
        Assert.That(diagnostics.Select(d => SourceAt(d, source)), Is.EquivalentTo(new[] { "Outer", "Formatter" }));
    }

    [Test]
    public async Task Analyzer_ShouldReportTheTypesInAnInferredType_WhenVarIsUsed()
    {
        // Arrange: nothing in the application names Repository, but `var` stands for List<Repository>
        const string yaml = """
            allowed:
              - from: MyApp.Application
                to: MyApp.Domain
              - from: MyApp.Application
                to: System.*
              - from: MyApp.Domain
                to: .*
            """;

        const string source = """
            namespace MyApp.Domain
            {
                public static class Catalog
                {
                    public static System.Collections.Generic.List<MyApp.Infrastructure.Repository> All() { return new(); }
                }
            }

            namespace MyApp.Application
            {
                using MyApp.Domain;

                public class Service
                {
                    public int Count() { var all = Catalog.All(); return all.Count; }
                }
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync([source, Infrastructure], yaml);

        // Assert
        Assert.That(diagnostics.Select(d => d.Id).ToArray(), Is.EqualTo(new[] { "DG0001" }));
        Assert.That(SourceAt(diagnostics[0], source), Is.EqualTo("var"));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("'MyApp.Infrastructure'"));
    }

    [Test]
    public async Task Analyzer_ShouldReportNoDiagnostics_WhenForeachAndCatchVariablesNameTheirType()
    {
        // Arrange: `string` needs no rule and the exception type comes with its using, so there is nothing to infer
        const string yaml = """
            allowed:
              - from: MyApp.Application
                to: MyApp.Infrastructure
              - from: MyApp.Infrastructure
                to: System
            """;

        const string source = """
            namespace MyApp.Infrastructure
            {
                public class StorageException : System.Exception { }
            }

            namespace MyApp.Application
            {
                using MyApp.Infrastructure;

                public class Service
                {
                    public void Run(string[] items)
                    {
                        foreach (string item in items) { }
                        try { } catch (StorageException exception) { }
                    }
                }
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync([source, Infrastructure], yaml);

        // Assert
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task Analyzer_ShouldReportTheFullNamespace_WhenNamespaceBlocksAreNested()
    {
        // Arrange
        const string source = """
            namespace MyApp
            {
                namespace Application
                {
                    using MyApp.Infrastructure;
                }
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync([source, Infrastructure], AllowAppToDomain);

        // Assert
        Assert.That(diagnostics.Select(d => d.Id).ToArray(), Is.EqualTo(new[] { "DG0001" }));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("'MyApp.Application' to depend on"));
    }

    [Test]
    public async Task Analyzer_ShouldUseTheRootNamespace_WhenCodeHasNoNamespace()
    {
        // Arrange: e.g. top-level statements, or a composition class next to them
        const string yaml = """
            allowed:
              - from: MyApp.Web
                to: MyApp.Domain
            """;

        const string source = """
            using MyApp.Domain;
            using MyApp.Infrastructure;

            internal static class Composition
            {
                public static Order Create() { return new Order(); }
                public static Repository Store() { return new Repository(); }
            }
            """;

        const string domain = """
            namespace MyApp.Domain
            {
                public class Order { }
            }
            """;

        Dictionary<string, string> properties = new() { ["RootNamespace"] = "MyApp.Web" };

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(
            [(string.Empty, source), (string.Empty, domain), (string.Empty, Infrastructure)],
            yaml,
            properties);

        // Assert: the using and the two uses of Repository
        Assert.That(diagnostics.Select(d => d.Id).ToArray(), Is.EqualTo(new[] { "DG0001", "DG0001", "DG0001" }));
        Assert.That(diagnostics.Select(d => d.GetMessage()), Has.All.Contains("'MyApp.Web' to depend on 'MyApp.Infrastructure'"));
    }

    [Test]
    public async Task Analyzer_ShouldUseTheAssemblyName_WhenThereIsNoRootNamespace()
    {
        // Arrange
        const string source = """
            using MyApp.Infrastructure;

            internal static class Composition { }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync([source, Infrastructure], AllowAppToDomain);

        // Assert
        Assert.That(
            diagnostics.Select(d => d.GetMessage()).ToArray(),
            Is.EqualTo(new[] { "No rule allows 'TestProject' to depend on 'MyApp.Infrastructure'." }));
    }

    [Test]
    public async Task Analyzer_ShouldReportTheProjectAsTarget_WhenTypeHasNoNamespace()
    {
        // Arrange
        const string yaml = """
            allowed:
              - from: MyApp.Application
                to: MyApp.Domain
            """;

        const string source = """
            internal static class Settings { }

            namespace MyApp.Application
            {
                public class Service { public string Name { get; } = nameof(Settings); }
            }
            """;

        Dictionary<string, string> properties = new() { ["RootNamespace"] = "MyApp.Web" };

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync([(string.Empty, source)], yaml, properties);

        // Assert
        Assert.That(
            diagnostics.Select(d => d.GetMessage()).ToArray(),
            Is.EqualTo(new[] { "No rule allows 'MyApp.Application' to depend on 'MyApp.Web'." }));
    }

    [Test]
    public async Task Analyzer_ShouldReportWhatARazorFileUses_WhenGeneratedCodeIsMappedToIt()
    {
        // Arrange: the C# Razor makes of a component; #line maps what was written in .razor files
        const string component = """
            // <auto-generated/>
            namespace MyApp.Components
            {
            #line 3 "Components/_Imports.razor"
            using MyApp.Infrastructure;

            #line default
            #line hidden
                public partial class Page
                {
                    private MyApp.Infrastructure.Repository? _hidden;
            #line default
                    private MyApp.Infrastructure.Repository? _unmapped;
            #line 12 "Components/Page.razor"
                    private MyApp.Infrastructure.Repository? _written;
            #line default
            #line hidden
                }
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(
            [("Page.razor.g.cs", component), ("Infrastructure.cs", Infrastructure)],
            AllowAppToDomain);

        // Assert: the using on line 3 of _Imports.razor and the field on line 12 of Page.razor (0-based: 2 and 11)
        IEnumerable<string> places = diagnostics.Select(d => d.Location.GetMappedLineSpan())
            .Select(span => $"{span.Path}:{span.StartLinePosition.Line}");
        Assert.That(places, Is.EquivalentTo(new[] { "Components/_Imports.razor:2", "Components/Page.razor:11" }));
    }

    [Test]
    public async Task Analyzer_ShouldReportAnImportsUsingOnce_WhenSeveralComponentsShareIt()
    {
        // Arrange
        static string Component(string name)
        {
            return $$"""
                // <auto-generated/>
                namespace MyApp.Components
                {
                #line 3 "Components/_Imports.razor"
                using MyApp.Infrastructure;

                #line default
                #line hidden
                    public partial class {{name}} { }
                }
                """;
        }

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(
            [("First.razor.g.cs", Component("First")), ("Second.razor.g.cs", Component("Second")), ("Infrastructure.cs", Infrastructure)],
            AllowAppToDomain);

        // Assert
        Assert.That(diagnostics.Select(d => d.Id).ToArray(), Is.EqualTo(new[] { "DG0001" }));
    }

    [Test]
    public async Task Analyzer_ShouldSkipGeneratedCode_WhenNothingMapsItToAFile()
    {
        // Arrange: e.g. what a source generator adds on its own
        const string generated = """
            // <auto-generated/>
            namespace MyApp.Application
            {
                using MyApp.Infrastructure;

                public class Generated { private Repository? _repository; }
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(
            [("Generated.g.cs", generated), ("Infrastructure.cs", Infrastructure)],
            AllowAppToDomain);

        // Assert
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task Analyzer_ShouldReportDG0004AtTheMistakeAndNothingElse_WhenRuleFileIsInvalid()
    {
        // Arrange: with a typo in the file, checking the code would only give confusing DG0001s
        const string yaml = """
            allowed:
              - form: MyApp.Application
                to: MyApp.Domain
            """;

        const string source = """
            namespace MyApp.Application
            {
                using MyApp.Infrastructure;
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync([source, Infrastructure], yaml);

        // Assert
        Assert.That(diagnostics.Select(d => d.Id).ToArray(), Is.EqualTo(new[] { "DG0004", "DG0004" }));
        FileLinePositionSpan place = diagnostics[0].Location.GetLineSpan();
        Assert.That(place.Path, Is.EqualTo(RoslynAnalyzer.ConfigFileName));
        Assert.That(place.StartLinePosition.Line, Is.EqualTo(1));
    }

    [Test]
    public async Task Analyzer_ShouldPointDG0003AtTheRules_WhenRulesConflict()
    {
        // Arrange: the allow starts on the second line of the file, the deny on the fifth (0-based: 1 and 4)
        const string yaml = """
            allowed:
              - from: MyApp.Application
                to: MyApp.Domain
            denied:
              - from: MyApp.Application
                to: MyApp.Domain
            """;

        const string source = """
            namespace MyApp.Application { }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(source, yaml);

        // Assert
        Assert.That(diagnostics.Select(d => d.Id).ToArray(), Is.EqualTo(new[] { "DG0003" }));
        Assert.That(diagnostics[0].Location.GetLineSpan().StartLinePosition.Line, Is.EqualTo(1));
        Assert.That(diagnostics[0].AdditionalLocations[0].GetLineSpan().StartLinePosition.Line, Is.EqualTo(4));
    }

    private static string SourceAt(Diagnostic diagnostic, string source)
    {
        TextSpan span = diagnostic.Location.SourceSpan;
        return source.Substring(span.Start, span.Length);
    }

    [Test]
    public async Task Analyzer_ShouldCheckAFileUsingForEveryNamespaceInTheFile_WhenUsingIsAboveThem()
    {
        // Arrange: the using serves the code without a namespace (the root namespace) and MyApp.Application alike
        const string yaml = """
            allowed:
              - from: MyApp.Application
                to: MyApp.Infrastructure
            """;

        const string source = """
            using MyApp.Infrastructure;

            internal static class Composition { }

            namespace MyApp.Application
            {
                public class Service { }
            }
            """;

        Dictionary<string, string> properties = new() { ["RootNamespace"] = "MyApp.Web" };

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(
            [(string.Empty, source), (string.Empty, Infrastructure)],
            yaml,
            properties);

        // Assert
        Assert.That(
            diagnostics.Select(d => d.GetMessage()).ToArray(),
            Is.EqualTo(new[] { "No rule allows 'MyApp.Web' to depend on 'MyApp.Infrastructure'." }));
    }

    [Test]
    public async Task Analyzer_ShouldNotReportAnInstanceMember_WhenItsClassIsNestedInAStaticClass()
    {
        // Arrange: `var` stands for Outer.Inner (reported once, there); reading its Value is no dependency of its own
        const string yaml = """
            allowed:
              - from: MyApp.Application
                to: MyApp.Domain
              - from: MyApp.Domain
                to: MyApp.Infrastructure
            """;

        const string source = """
            namespace MyApp.Infrastructure
            {
                public static class Settings { public class Item { public int Value => 1; } }
            }

            namespace MyApp.Domain
            {
                public static class Factory { public static MyApp.Infrastructure.Settings.Item Create() { return new(); } }
            }

            namespace MyApp.Application
            {
                using MyApp.Domain;

                public class Service
                {
                    public int Read() { var item = Factory.Create(); return item.Value; }
                }
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync(source, yaml);

        // Assert
        Assert.That(diagnostics.Select(d => SourceAt(d, source)).ToArray(), Is.EqualTo(new[] { "var" }));
    }

    [Test]
    public async Task Analyzer_ShouldGiveTheNamespacesAsProperties_WhenDependencyIsNotAllowed()
    {
        // Arrange: tools read them instead of the message (the CLI's generate collects them)
        const string source = """
            namespace MyApp.Application
            {
                using MyApp.Infrastructure;
            }
            """;

        // Act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerRunner.GetDiagnosticsAsync([source, Infrastructure], AllowAppToDomain);

        // Assert
        Assert.That(diagnostics.Select(d => d.Id).ToArray(), Is.EqualTo(new[] { "DG0001" }));
        Assert.That(diagnostics[0].Properties[RoslynAnalyzer.SourceNamespaceProperty], Is.EqualTo("MyApp.Application"));
        Assert.That(diagnostics[0].Properties[RoslynAnalyzer.TargetNamespaceProperty], Is.EqualTo("MyApp.Infrastructure"));
    }
}
