using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyGuard.Tests.Analyzer.Infrastructure;

internal static class AnalyzerRunner
{
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
        IEnumerable<(string path, string text)> additionalFiles = yamlConfig is null
            ? []
            : [(DependencyGuard.Analyzer.Analyzer.ConfigFileName, yamlConfig)];

        return GetDiagnosticsAsync(sources, additionalFiles);
    }

    internal static Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
        string source,
        IEnumerable<(string path, string text)> additionalFiles)
    {
        return GetDiagnosticsAsync([source], additionalFiles);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
        string[] sources,
        IEnumerable<(string path, string text)> additionalFiles)
    {
        SyntaxTree[] syntaxTrees = [.. sources.Select(s => CSharpSyntaxTree.ParseText(s))];

        MetadataReference[] references =
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
        ];

        CSharpCompilation compilation = CSharpCompilation.Create(
            "TestProject",
            syntaxTrees,
            references,
            new(OutputKind.DynamicallyLinkedLibrary));

        ImmutableArray<AdditionalText> additionalTexts =
        [
            ..additionalFiles.Select(AdditionalText (f) => new AdditionalTextInMemory(f.path, f.text))
        ];

        CompilationWithAnalyzers compilationWithAnalyzers = compilation.WithAnalyzers(
            [new DependencyGuard.Analyzer.Analyzer()],
            new AnalyzerOptions(additionalTexts));

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}
