using System.Collections.Immutable;
using DependencyGuard.Core.Interfaces;
using DependencyGuard.Roslyn.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace DependencyGuard.Roslyn.Interfaces;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RoslynAnalyzer : DiagnosticAnalyzer
{
    public const string ConfigFileName = "dependency-guard.yaml";
    public const string SourceNamespaceProperty = "SourceNamespace";
    public const string TargetNamespaceProperty = "TargetNamespace";

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
        IReadOnlyList<AdditionalText> configFiles = _ruleFiles ?? GetConfigFiles(contextStart);
        if (!TryCreateRuleSets(contextStart, configFiles, out List<DependencyRuleSet> ruleSets))
        {
            return;
        }

        if (ruleSets.Count == 0)
        {
            contextStart.RegisterCompilationEndAction(contextEnd =>
            {
                Diagnostic configMissingDiagnostics = Diagnostic.Create(RoslynAnalyzerDiagnostics.WarningConfigurationMissing, Location.None);
                contextEnd.ReportDiagnostic(configMissingDiagnostics);
            });
            return;
        }

        IReadOnlyList<RuleConflict> conflicts = DependencyGuardFactory.TryCreateAnalyzer(
            ruleSets,
            out IDependencyAnalyzer? analyzer);

        if (conflicts.Count > 0)
        {
            contextStart.RegisterCompilationEndAction(contextEnd => ReportConflicts(conflicts, configFiles, contextEnd));
            return;
        }

        if (analyzer == null)
        {
            throw new NullReferenceException("Analyzer can't be null when there are no conflicts.");
        }

        RoslynAnalyzerCompilation compilation = new(
            analyzer,
            configFiles,
            GetRootNamespace(contextStart),
            contextStart.Compilation.Assembly);

        contextStart.RegisterSyntaxNodeAction(compilation.AnalyzeUsingDirective, SyntaxKind.UsingDirective);
        contextStart.RegisterSyntaxNodeAction(compilation.AnalyzeName, SyntaxKind.IdentifierName, SyntaxKind.GenericName);
    }

    private static bool TryCreateRuleSets(
#pragma warning disable RS1013
        CompilationStartAnalysisContext contextStart,
#pragma warning restore RS1013
        IReadOnlyList<AdditionalText> configFiles,
        out List<DependencyRuleSet> ruleSets)
    {
        ruleSets = [];
        List<Diagnostic> errors = [];
        foreach (AdditionalText configFile in configFiles)
        {
            string? configText = configFile.GetText()?.ToString();
            if (configText is null)
            {
                continue;
            }

            try
            {
                ruleSets.Add(DependencyGuardFactory.ParseFromYaml(configText, configFile.Path));
            }
            catch (RuleSetException exception)
            {
                errors.AddRange(exception.Errors.Select(error => Diagnostic.Create(
                    RoslynAnalyzerDiagnostics.ErrorInvalidRuleFile,
                    error.Location is null
                        ? Location.None
                        : ToLocation(error.Location, configFiles),
                    error.Message)));
            }
            catch (Exception exception)
            {
                errors.Add(Diagnostic.Create(RoslynAnalyzerDiagnostics.ErrorInvalidRuleFile, Location.None, $"{configFile.Path}: {exception.Message}"));
            }
        }

        if (errors.Count == 0)
        {
            return true;
        }

        contextStart.RegisterCompilationEndAction(contextEnd =>
        {
            foreach (Diagnostic error in errors)
            {
                contextEnd.ReportDiagnostic(error);
            }
        });
        return false;
    }

#pragma warning disable RS1012
    private static IReadOnlyList<AdditionalText> GetConfigFiles(CompilationStartAnalysisContext contextStart)
#pragma warning restore RS1012
    {
        return contextStart.Options.AdditionalFiles
            .Where(f => string.Equals(Path.GetFileName(f.Path), ConfigFileName, StringComparison.OrdinalIgnoreCase))
            .GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToArray();
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

    private static void ReportConflicts(
        IReadOnlyList<RuleConflict> conflicts,
        IReadOnlyList<AdditionalText> configFiles,
        CompilationAnalysisContext contextEnd)
    {
        foreach (RuleConflict conflict in conflicts)
        {
            Location primary = conflict.PrimaryLocation is not null
                ? ToLocation(conflict.PrimaryLocation, configFiles)
                : Location.None;

            Location[] additional = conflict.SecondaryLocation is not null
                ? [ToLocation(conflict.SecondaryLocation, configFiles)]
                : [];

            Diagnostic conflictingRulesDiagnostic = Diagnostic.Create(RoslynAnalyzerDiagnostics.ErrorConflictingRules,
                primary,
                additional,
                null,
                conflict.Message);

            contextEnd.ReportDiagnostic(conflictingRulesDiagnostic);
        }
    }

    internal static Location ToLocation(SourceLocation loc, IReadOnlyList<AdditionalText> configFiles)
    {
        AdditionalText? configFile = configFiles
            .FirstOrDefault(f => string.Equals(f.Path, loc.FilePath, StringComparison.OrdinalIgnoreCase));

        if (configFile is null)
        {
            return Location.None;
        }

        SourceText? text = configFile.GetText();
        if (text is null || loc.Line >= text.Lines.Count)
        {
            return Location.None;
        }

        TextLine line = text.Lines[loc.Line];
        int start = Math.Min(line.Start + loc.Column, line.End);
        TextSpan span = new(start, 0);
        LinePositionSpan lineSpan = new(new(loc.Line, loc.Column), new(loc.Line, loc.Column));

        return Location.Create(configFile.Path, span, lineSpan);
    }
}
