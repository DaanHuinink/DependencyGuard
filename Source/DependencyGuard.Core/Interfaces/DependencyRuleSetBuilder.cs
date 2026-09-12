namespace DependencyGuard.Core.Interfaces;

public sealed class DependencyRuleSetBuilder
{
    private readonly HashSet<(string From, string To, DependencyAction Action)> _rules = [];
    private readonly Dictionary<string, HashSet<string>> _exposedTo = [];

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

    public DependencyRuleSetBuilder AddExposedTo(string ns, IEnumerable<string> consumers)
    {
        if (!_exposedTo.TryGetValue(ns, out HashSet<string>? existing))
        {
            existing = [];
            _exposedTo[ns] = existing;
        }
        existing.UnionWith(consumers);
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

        List<ExposedToRule> exposedToRules = _exposedTo
            .OrderBy(e => e.Key, StringComparer.Ordinal)
            .Select(kv => new ExposedToRule(
                kv.Key,
                kv.Value.OrderBy(c => c, StringComparer.Ordinal).ToList()))
            .ToList();

        return new(rules, exposedToRules.Count > 0 ? exposedToRules : null);
    }
}
