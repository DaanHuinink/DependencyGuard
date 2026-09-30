using DependencyGuard.Core.Interfaces;
using Microsoft.CodeAnalysis;

namespace DependencyGuard.Roslyn.Internal;

internal sealed record RoslynDependencyAnalyzerRules(IDependencyAnalyzer? Analyzer, IReadOnlyList<Diagnostic> Problems);
