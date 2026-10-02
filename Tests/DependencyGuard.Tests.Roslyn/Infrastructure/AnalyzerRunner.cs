using System.Collections.Immutable;
using DependencyGuard.Roslyn.Composition;
using DependencyGuard.Roslyn.Interfaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyGuard.Tests.Roslyn.Infrastructure;

internal static class AnalyzerRunner
{
    private static readonly Lazy<MetadataReference[]> FrameworkReferences = new(() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray());

    internal static Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
        string source,
        string? yamlConfig = null)
    {
        return GetDiagnosticsAsync([source], yamlConfig);
    }

    internal static Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
        string[] sources,
        string? yamlConfig)
    {
        (string path, string text)[] files = sources
            .Select(s => (string.Empty, s))
            .ToArray();
        return GetDiagnosticsAsync(files, yamlConfig);
    }

    internal static Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
        string source,
        IEnumerable<(string path, string text)> additionalFiles)
    {
        return GetDiagnosticsAsync([(string.Empty, source)], additionalFiles, new Dictionary<string, string>());
    }

    internal static Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
        (string path, string text)[] sources,
        string? yamlConfig,
        IReadOnlyDictionary<string, string>? buildProperties = null)
    {
        IEnumerable<(string path, string text)> additionalFiles = yamlConfig is null
            ? []
            : [(DependencyRoslynAnalyzerContract.ConfigFileName, yamlConfig)];

        return GetDiagnosticsAsync(sources, additionalFiles, buildProperties ?? new Dictionary<string, string>());
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
        (string path, string text)[] sources,
        IEnumerable<(string path, string text)> additionalFiles,
        IReadOnlyDictionary<string, string> buildProperties)
    {
        SyntaxTree[] syntaxTrees = sources
            .Select(s => CSharpSyntaxTree.ParseText(s.text, path: s.path))
            .ToArray();

        CSharpCompilation compilation = CSharpCompilation.Create(
            "TestProject",
            syntaxTrees,
            FrameworkReferences.Value,
            new(OutputKind.DynamicallyLinkedLibrary));

        ImmutableArray<AdditionalText> additionalTexts = additionalFiles
            .Select(AdditionalText (f) => new AdditionalTextInMemory(f.path, f.text))
            .ToImmutableArray();

        Dictionary<string, string> globalOptions = buildProperties.ToDictionary(p => "build_property." + p.Key, p => p.Value);

        CompilationWithAnalyzers compilationWithAnalyzers = compilation.WithAnalyzers(
            [DependencyRoslynAnalyzerFactory.Create()],
            new AnalyzerOptions(additionalTexts, new AnalyzerConfigOptionsProviderInMemory(globalOptions)));

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}
