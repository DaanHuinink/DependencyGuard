namespace DependencyGuard.Core.Interfaces;

public sealed record DependencyResult
(
    bool IsAllowed,
    string? Reason,
    SourceLocation? RuleLocation = null
);

public sealed record RuleConflict
(
    string Message,
    SourceLocation? PrimaryLocation = null,
    SourceLocation? SecondaryLocation = null
);

public sealed record NamespaceDependency(string SourceNamespace, string TargetNamespace);

public interface IDependencyAnalyzer
{
    DependencyResult AnalyzeDependency(NamespaceDependency dependency);
}
