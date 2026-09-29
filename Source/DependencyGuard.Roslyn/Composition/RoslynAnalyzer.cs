using System.Collections.Immutable;
using DependencyGuard.Roslyn.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyGuard.Roslyn.Composition;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RoslynAnalyzer : DiagnosticAnalyzer
{
    private const string RootNamespaceOption = "build_property.RootNamespace";

    private readonly IReadOnlyList<AdditionalText>? _ruleFiles;

    public RoslynAnalyzer()
    {
    }

    public RoslynAnalyzer(IReadOnlyList<AdditionalText> ruleFiles)
    {
        _ruleFiles = ruleFiles;
    }

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
    [
        RoslynAnalyzerDiagnostics.WarningDisallowedDependency,
        RoslynAnalyzerDiagnostics.WarningConfigurationMissing,
        RoslynAnalyzerDiagnostics.ErrorConflictingRules,
        RoslynAnalyzerDiagnostics.ErrorInvalidRuleFile,
        RoslynAnalyzerDiagnostics.ErrorUnhandledException
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
            contextStart.RegisterCompilationEndAction(contextEnd =>
                contextEnd.ReportDiagnostic(Diagnostic.Create(RoslynAnalyzerDiagnostics.ErrorUnhandledException, Location.None, exception)));
        }
    }

    private void RegisterAnalyzer(CompilationStartAnalysisContext contextStart)
    {
        IReadOnlyList<AdditionalText> ruleFiles = _ruleFiles ?? RoslynAnalyzerRuleFiles.Find(contextStart.Options.AdditionalFiles);
        RoslynAnalyzerRules rules = RoslynAnalyzerRuleFiles.Read(ruleFiles);
        if (rules.Analyzer is null)
        {
            contextStart.RegisterCompilationEndAction(contextEnd => ReportProblems(contextEnd, rules.Problems));
            return;
        }

        RoslynAnalyzerCompilation compilation = new(
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
