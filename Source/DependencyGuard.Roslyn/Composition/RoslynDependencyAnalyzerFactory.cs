using DependencyGuard.Roslyn.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyGuard.Roslyn.Composition;

public static class RoslynDependencyAnalyzerFactory
{
    public static DiagnosticAnalyzer Create()
    {
        return new RoslynDependencyAnalyzer();
    }

    public static DiagnosticAnalyzer Create(IReadOnlyList<AdditionalText> ruleFiles)
    {
        return new RoslynDependencyAnalyzer(ruleFiles);
    }
}
