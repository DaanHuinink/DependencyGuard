using DependencyGuard.Roslyn.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyGuard.Roslyn.Composition;

public static class DependencyRoslynAnalyzerFactory
{
    public static DiagnosticAnalyzer Create()
    {
        return new DependencyRoslynAnalyzer();
    }

    public static DiagnosticAnalyzer Create(IReadOnlyList<AdditionalText> ruleFiles)
    {
        return new DependencyRoslynAnalyzer(ruleFiles);
    }
}
