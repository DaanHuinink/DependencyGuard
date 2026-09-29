using DependencyGuard.Core.Interfaces;
using Microsoft.CodeAnalysis;

namespace DependencyGuard.Roslyn.Internal;

internal sealed record RoslynAnalyzerRules(IDependencyAnalyzer? Analyzer, IReadOnlyList<Diagnostic> Problems);
