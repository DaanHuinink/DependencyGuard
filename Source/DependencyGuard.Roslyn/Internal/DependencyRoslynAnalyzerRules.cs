using DependencyGuard.Core.Interfaces;
using Microsoft.CodeAnalysis;

namespace DependencyGuard.Roslyn.Internal;

internal sealed record DependencyRoslynAnalyzerRules(IDependencyAnalyzer? Analyzer, IReadOnlyList<Diagnostic> Problems);
