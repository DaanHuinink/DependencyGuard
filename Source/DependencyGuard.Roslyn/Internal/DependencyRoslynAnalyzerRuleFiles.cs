using DependencyGuard.Core.Composition;
using DependencyGuard.Core.Interfaces;
using DependencyGuard.Roslyn.Interfaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DependencyGuard.Roslyn.Internal;

internal static class DependencyRoslynAnalyzerRuleFiles
{
    public static IReadOnlyList<AdditionalText> Find(IEnumerable<AdditionalText> additionalFiles)
    {
        return additionalFiles
            .Where(f => string.Equals(Path.GetFileName(f.Path), DependencyRoslynAnalyzerContract.ConfigFileName, StringComparison.OrdinalIgnoreCase))
            .GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToArray();
    }

    public static DependencyRoslynAnalyzerRules Read(IReadOnlyList<AdditionalText> ruleFiles)
    {
        List<DependencyRuleSet> ruleSets = [];
        List<Diagnostic> mistakes = [];
        foreach (AdditionalText ruleFile in ruleFiles)
        {
            string? text = ruleFile.GetText()?.ToString();
            if (text is null)
            {
                continue;
            }

            try
            {
                ruleSets.Add(DependencyGuardFactory.ParseFromYaml(text, ruleFile.Path));
            }
            catch (RuleSetException exception)
            {
                mistakes.AddRange(exception.Errors.Select(error => ToDiagnostic(error, ruleFiles)));
            }
            catch (Exception exception)
            {
                mistakes.Add(Diagnostic.Create(
                    DependencyRoslynAnalyzerDiagnostics.ErrorInvalidRuleFile,
                    Location.None,
                    $"{ruleFile.Path}: {exception.Message}"));
            }
        }

        if (mistakes.Count > 0)
        {
            return new(null, mistakes);
        }

        if (ruleSets.Count == 0)
        {
            return new(null, [Diagnostic.Create(DependencyRoslynAnalyzerDiagnostics.WarningConfigurationMissing, Location.None)]);
        }

        IReadOnlyList<RuleConflict> conflicts = DependencyGuardFactory.TryCreateAnalyzer(
            ruleSets,
            out IDependencyAnalyzer? analyzer);

        if (conflicts.Count > 0)
        {
            return new(
                null,
                conflicts
                    .Select(conflict => ToDiagnostic(conflict, ruleFiles))
                    .ToArray());
        }

        if (analyzer == null)
        {
            throw new NullReferenceException("Analyzer can't be null when there are no conflicts.");
        }

        return new(analyzer, []);
    }

    public static Location ToLocation(SourceLocation loc, IReadOnlyList<AdditionalText> ruleFiles)
    {
        AdditionalText? ruleFile = ruleFiles
            .FirstOrDefault(f => string.Equals(f.Path, loc.FilePath, StringComparison.OrdinalIgnoreCase));

        if (ruleFile is null)
        {
            return Location.None;
        }

        SourceText? text = ruleFile.GetText();
        if (text is null || loc.Line >= text.Lines.Count)
        {
            return Location.None;
        }

        TextLine line = text.Lines[loc.Line];
        int start = Math.Min(line.Start + loc.Column, line.End);
        TextSpan span = new(start, 0);
        LinePositionSpan lineSpan = new(new(loc.Line, loc.Column), new(loc.Line, loc.Column));

        return Location.Create(ruleFile.Path, span, lineSpan);
    }

    private static Diagnostic ToDiagnostic(RuleSetError error, IReadOnlyList<AdditionalText> ruleFiles)
    {
        Location location = error.Location is null
            ? Location.None
            : ToLocation(error.Location, ruleFiles);
        return Diagnostic.Create(DependencyRoslynAnalyzerDiagnostics.ErrorInvalidRuleFile, location, error.Message);
    }

    private static Diagnostic ToDiagnostic(RuleConflict conflict, IReadOnlyList<AdditionalText> ruleFiles)
    {
        Location primary = conflict.PrimaryLocation is not null
            ? ToLocation(conflict.PrimaryLocation, ruleFiles)
            : Location.None;

        Location[] additional = conflict.SecondaryLocation is not null
            ? [ToLocation(conflict.SecondaryLocation, ruleFiles)]
            : [];

        return Diagnostic.Create(DependencyRoslynAnalyzerDiagnostics.ErrorConflictingRules,
            primary,
            additional,
            null,
            conflict.Message);
    }
}
