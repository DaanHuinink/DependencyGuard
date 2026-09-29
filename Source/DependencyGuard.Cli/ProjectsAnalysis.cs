using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace DependencyGuard.Cli;

internal sealed record ProjectAnalysis(ImmutableArray<Diagnostic> Diagnostics, int CompileErrors);

// Runs DependencyGuard's analyzer on a project's compilation with the given rule files, as the build does.
internal static class ProjectsAnalysis
{
    // The rule files the build gives the project (DependencyGuardConfig, DependencyGuardConfigPath), or else the
    // dependency-guard.yaml in its folder (a project without the DependencyGuard package); the --config files on top.
    public static IReadOnlyList<AdditionalText> GetRuleFiles(Project project, IReadOnlyList<string> configPaths)
    {
        List<AdditionalText> ruleFiles = project.AnalyzerOptions.AdditionalFiles
            .Where(f => string.Equals(Path.GetFileName(f.Path), Analyzer.Analyzer.ConfigFileName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        string projectFile = Path.Combine(Path.GetDirectoryName(project.FilePath)!, Analyzer.Analyzer.ConfigFileName);
        if (ruleFiles.Count == 0 && File.Exists(projectFile))
        {
            ruleFiles.Add(new AdditionalTextFile(projectFile));
        }

        ruleFiles.AddRange(configPaths.Select(path => new AdditionalTextFile(path)));
        return ruleFiles
            .GroupBy(f => Path.GetFullPath(f.Path), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    // An empty rule file allows nothing, so every dependency is reported: that is what generate collects.
    public static IReadOnlyList<AdditionalText> GetEmptyRuleFile(Project project)
    {
        string path = Path.Combine(Path.GetDirectoryName(project.FilePath)!, "generate", Analyzer.Analyzer.ConfigFileName);
        return [new AdditionalTextFile(path, text: string.Empty)];
    }

    public static async Task<ProjectAnalysis> AnalyzeAsync(Project project, IReadOnlyList<AdditionalText> ruleFiles)
    {
        Compilation compilation = await project.GetCompilationAsync()
                                  ?? throw new InvalidOperationException($"{project.Name} has no compilation.");
        int compileErrors = compilation.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error);

        // The rule files go to the analyzer itself: a --config file may have any name.
        AnalyzerOptions options = new(
            [],
            new OptionsWithRootNamespace(project.AnalyzerOptions.AnalyzerConfigOptionsProvider, project.DefaultNamespace));
        ImmutableArray<Diagnostic> diagnostics = await compilation
            .WithAnalyzers([new Analyzer.Analyzer(ruleFiles)], options)
            .GetAnalyzerDiagnosticsAsync();

        return new(diagnostics, compileErrors);
    }

    private sealed class AdditionalTextFile(string path, string? text = null) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default)
        {
            return SourceText.From(text ?? File.ReadAllText(Path));
        }
    }

    // The project's analyzer options, with its RootNamespace when MSBuild does not hand it to analyzers itself (the
    // DependencyGuard package makes it do so; a project without the package does not).
    private sealed class OptionsWithRootNamespace(AnalyzerConfigOptionsProvider inner, string? rootNamespace)
        : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new GlobalOptionsWithRootNamespace(inner.GlobalOptions, rootNamespace);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            return inner.GetOptions(tree);
        }

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            return inner.GetOptions(textFile);
        }
    }

    private sealed class GlobalOptionsWithRootNamespace(AnalyzerConfigOptions inner, string? rootNamespace) : AnalyzerConfigOptions
    {
        private const string RootNamespaceKey = "build_property.RootNamespace";

        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
        {
            if (inner.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            value = key == RootNamespaceKey ? rootNamespace : null;
            return !string.IsNullOrWhiteSpace(value);
        }
    }
}
