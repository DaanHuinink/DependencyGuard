namespace DependencyGuard.Core.Interfaces;

public enum DependencyAction
{
    Allow,
    Deny
}

// Line and Column are 0-based.
public sealed record SourceLocation(string FilePath, int Line, int Column);

public sealed record DependencyRuleSet
(
    IReadOnlyList<DependencyRule> Rules,
    IReadOnlyList<ExposedToRule>? ExposedToRules = null
);

public sealed record DependencyRule
(
    string FromNamespace,
    string ToNamespace,
    DependencyAction Action,
    SourceLocation? SourceLocation = null
);

public sealed record ExposedToRule
(
    string Namespace,
    IReadOnlyList<string> Consumers,
    SourceLocation? SourceLocation = null);

public interface IDependencyRuleSetParser
{
    // ReSharper disable once UnusedMemberInSuper.Global
    DependencyRuleSet Parse(string configurationText, string? sourcePath = null);
}
