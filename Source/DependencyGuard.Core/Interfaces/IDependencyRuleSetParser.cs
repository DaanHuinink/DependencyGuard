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
    IReadOnlyList<DependencyRule> Rules
);

public sealed record DependencyRule
(
    string FromNamespace,
    string ToNamespace,
    DependencyAction Action,
    SourceLocation? SourceLocation = null
);

// A mistake in a rule file: a key that means nothing, a missing field or a pattern that can never match.
public sealed record RuleSetError(string Message, SourceLocation? Location = null);

// Thrown by the parser when a rule file has mistakes; Errors lists all of them, not only the first.
public sealed class RuleSetException(IReadOnlyList<RuleSetError> errors) : Exception(Describe(errors))
{
    public IReadOnlyList<RuleSetError> Errors { get; } = errors;

    private static string Describe(IReadOnlyList<RuleSetError> errors)
    {
        return string.Join(Environment.NewLine, errors.Select(e => e.Location is null
            ? e.Message
            : $"{e.Location.FilePath}({e.Location.Line + 1},{e.Location.Column + 1}): {e.Message}"));
    }
}

public interface IDependencyRuleSetParser
{
    // Throws RuleSetException when the text has mistakes.
    // ReSharper disable once UnusedMemberInSuper.Global
    DependencyRuleSet Parse(string configurationText, string? sourcePath = null);
}
