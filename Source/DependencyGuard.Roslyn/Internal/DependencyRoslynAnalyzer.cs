using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyGuard.Roslyn.Internal;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class DependencyRoslynAnalyzer : DiagnosticAnalyzer
{
    private const string RootNamespaceOption = "build_property.RootNamespace";

    private readonly IReadOnlyList<AdditionalText>? _ruleFiles;

    public DependencyRoslynAnalyzer()
    {
    }

    public DependencyRoslynAnalyzer(IReadOnlyList<AdditionalText> ruleFiles)
    {
        _ruleFiles = ruleFiles;
    }

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
    [
        DependencyRoslynAnalyzerDiagnostics.WarningDisallowedDependency,
        DependencyRoslynAnalyzerDiagnostics.WarningConfigurationMissing,
        DependencyRoslynAnalyzerDiagnostics.ErrorConflictingRules,
        DependencyRoslynAnalyzerDiagnostics.ErrorInvalidRuleFile,
        DependencyRoslynAnalyzerDiagnostics.ErrorUnhandledException
    ];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private void OnCompilationStart(CompilationStartAnalysisContext contextStart)
    {
        try
        {
            RegisterAnalyzer(contextStart);
        }
        catch (Exception exception)
        {
            Diagnostic problem = Diagnostic.Create(DependencyRoslynAnalyzerDiagnostics.ErrorUnhandledException, Location.None, exception);
            contextStart.RegisterCompilationEndAction(contextEnd => contextEnd.ReportDiagnostic(problem));
        }
    }

    private void RegisterAnalyzer(CompilationStartAnalysisContext contextStart)
    {
        IReadOnlyList<AdditionalText> ruleFiles = _ruleFiles ?? DependencyRoslynAnalyzerRuleFiles.Find(contextStart.Options.AdditionalFiles);
        DependencyRoslynAnalyzerRules rules = DependencyRoslynAnalyzerRuleFiles.Read(ruleFiles);
        if (rules.Analyzer is null)
        {
            contextStart.RegisterCompilationEndAction(contextEnd => ReportProblems(contextEnd, rules.Problems));
            return;
        }

        DependencyRoslynAnalyzerCompilation compilation = new(
            rules.Analyzer,
            ruleFiles,
            GetRootNamespace(contextStart),
            contextStart.Compilation.Assembly);

        contextStart.RegisterSyntaxNodeAction(compilation.AnalyzeUsingDirective, SyntaxKind.UsingDirective);
        contextStart.RegisterSyntaxNodeAction(compilation.AnalyzeName, SyntaxKind.IdentifierName, SyntaxKind.GenericName);
    }

    private static void ReportProblems(CompilationAnalysisContext contextEnd, IReadOnlyList<Diagnostic> problems)
    {
        foreach (Diagnostic problem in problems)
        {
            contextEnd.ReportDiagnostic(problem);
        }
    }

#pragma warning disable RS1012
    private static string GetRootNamespace(CompilationStartAnalysisContext contextStart)
#pragma warning restore RS1012
    {
        return contextStart.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(RootNamespaceOption, out string? rootNamespace)
               && !string.IsNullOrWhiteSpace(rootNamespace)
            ? rootNamespace.Trim()
            : contextStart.Compilation.AssemblyName ?? string.Empty;
    }
}
