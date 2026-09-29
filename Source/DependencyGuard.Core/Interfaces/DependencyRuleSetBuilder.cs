namespace DependencyGuard.Core.Interfaces;

public sealed class DependencyRuleSetBuilder
{
    private readonly HashSet<(string From, string To, DependencyAction Action)> _rules = [];

    public DependencyRuleSetBuilder AddAllow(string from, string to)
    {
        _rules.Add((from, to, DependencyAction.Allow));
        return this;
    }

    public DependencyRuleSetBuilder AddDeny(string from, string to)
    {
        _rules.Add((from, to, DependencyAction.Deny));
        return this;
    }

    public DependencyRuleSet Build()
    {
        List<DependencyRule> rules = _rules
            .OrderBy(r => r.From, StringComparer.Ordinal)
            .ThenBy(r => r.To, StringComparer.Ordinal)
            .ThenBy(r => r.Action)
            .Select(r => new DependencyRule(r.From, r.To, r.Action))
            .ToList();

        return new(rules);
    }
}
