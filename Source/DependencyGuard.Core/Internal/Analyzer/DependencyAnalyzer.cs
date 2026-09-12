using DependencyGuard.Core.Interfaces;

namespace DependencyGuard.Core.Internal.Analyzer;

internal sealed class DependencyAnalyzer(DependencyRuleSet ruleSet) : IDependencyAnalyzer
{
    public DependencyResult AnalyzeDependency(NamespaceDependency dependency)
    {
        string source = dependency.SourceNamespace;
        string target = dependency.TargetNamespace;

        // Priority 1: explicit allowed/denied rule.
        // When multiple rules match, the most specific `to` pattern wins; `from` is the tiebreaker.
        DependencyRule? explicitRule = ruleSet.Rules
            .Where(r => NamespaceMatches(source, r.FromNamespace)
                     && NamespaceMatches(target, r.ToNamespace))
            .OrderByDescending(r => PatternBaseLength(r.ToNamespace))
            .ThenByDescending(r => PatternBaseLength(r.FromNamespace))
            .FirstOrDefault();

        if (explicitRule is not null)
        {
            return ToRuleResult(explicitRule, source, target);
        }

        // Priority 2: exposedTo, which restricts which namespaces may consume the target.
        ExposedToRule? applicableExposedTo = (ruleSet.ExposedToRules ?? [])
            .Where(e => NamespaceMatches(target, e.Namespace))
            .OrderByDescending(e => PatternBaseLength(e.Namespace))
            .FirstOrDefault();

        if (applicableExposedTo is not null)
        {
            bool isPermittedConsumer = applicableExposedTo.Consumers
                .Any(c => NamespaceMatches(source, c));

            if (!isPermittedConsumer)
            {
                string consumers = string.Join(", ", applicableExposedTo.Consumers.Select(c => $"'{c}'"));
                return new(false,
                    $"'{applicableExposedTo.Namespace}' is only accessible to {consumers}; " +
                    $"'{source}' is not a permitted consumer.");
            }

            return new(true, null);
        }

        return new(false, $"No rule allows '{source}' to depend on '{target}'.");
    }

    private static DependencyResult ToRuleResult(DependencyRule rule, string source, string target)
    {
        return rule.Action == DependencyAction.Allow
            ? new(true, null)
            : new(false, $"Dependency from '{source}' to '{target}' is explicitly denied.", rule.SourceLocation);
    }


    private static bool NamespaceMatches(string subject, string pattern)
    {
        // .* means matches everything.
        if (pattern.Equals(".*", StringComparison.Ordinal))
        {
            return true;
        }

        // A bare pattern (no `.*` suffix) matches only the exact namespace.
        // A wildcard pattern (X.*) matches X itself and all its descendants.
        if (pattern.EndsWith(".*", StringComparison.Ordinal))
        {
            string prefix = pattern.Substring(0, pattern.Length - 2);
            return subject.Equals(prefix, StringComparison.Ordinal) ||
                   subject.StartsWith(prefix + ".", StringComparison.Ordinal);
        }
        return subject.Equals(pattern, StringComparison.Ordinal);
    }

    // Base length for specificity ordering: strip `.*` suffix from wildcard patterns.
    private static int PatternBaseLength(string pattern)
    {
        return pattern.EndsWith(".*", StringComparison.Ordinal) ? pattern.Length - 2 : pattern.Length;
    }
}
