using DependencyGuard.Core.Interfaces;

namespace DependencyGuard.Core.Internal.Validation;

internal sealed class RuleSetValidator
{
    public IReadOnlyList<RuleConflict> Validate(DependencyRuleSet ruleSet)
    {
        return ValidateExactRuleConflicts(ruleSet)
            .Concat(ValidateCrossingSpecificityConflicts(ruleSet))
            .Concat(ValidateEqualSpecificityConflicts(ruleSet))
            .ToArray();
    }

    private static IEnumerable<RuleConflict> ValidateExactRuleConflicts(DependencyRuleSet ruleSet)
    {
        foreach (IGrouping<(string, string), DependencyRule> group in ruleSet.Rules
            .GroupBy(r => (r.FromNamespace, r.ToNamespace))
            .Where(g => g
                .Select(r => r.Action)
                .Distinct()
                .Count() > 1))
        {
            DependencyRule allow = group.First(r => r.Action == DependencyAction.Allow);
            DependencyRule deny = group.First(r => r.Action == DependencyAction.Deny);
            string msg = $"Rule for '{group.Key.Item1}' → '{group.Key.Item2}' is declared as both allowed and denied.";
            yield return new(msg, allow.SourceLocation, deny.SourceLocation);
        }
    }

    private static IEnumerable<RuleConflict> ValidateCrossingSpecificityConflicts(DependencyRuleSet ruleSet)
    {
        // Rules are ranked by TO length first, then FROM length. When an allow and a deny
        // have "crossed" specificity (one is more specific in FROM, the other in TO), the
        // TO-wins ordering produces unintuitive results for the intersection of their ranges.
        DependencyRule[] allows = ruleSet.Rules
            .Where(r => r.Action == DependencyAction.Allow)
            .ToArray();
        DependencyRule[] denies = ruleSet.Rules
            .Where(r => r.Action == DependencyAction.Deny)
            .ToArray();

        foreach (DependencyRule allow in allows)
        {
            foreach (DependencyRule deny in denies)
            {
                // Case A: allow FROM is more specific (child of deny FROM),
                //         deny TO is more specific (child of allow TO).
                //         → deny wins for (allow.FROM → deny.TO) even though allow explicitly
                //           targets the narrower source namespace.
                if (IsWildcardParentOf(deny.FromNamespace, allow.FromNamespace) &&
                    IsWildcardParentOf(allow.ToNamespace, deny.ToNamespace))
                {
                    yield return new(
                        $"Allow rule ('from: {allow.FromNamespace}, to: {allow.ToNamespace}') is overridden by " +
                        $"deny rule ('from: {deny.FromNamespace}, to: {deny.ToNamespace}'): for dependencies " +
                        $"from '{allow.FromNamespace}' to '{deny.ToNamespace}', the deny wins because its " +
                        $"target namespace is more specific, despite the allow having a more specific source.",
                        allow.SourceLocation,
                        deny.SourceLocation);
                }

                // Case B: deny FROM is more specific (child of allow FROM),
                //         allow TO is more specific (child of deny TO).
                //         → allow wins for (deny.FROM → allow.TO) even though deny explicitly
                //           targets the narrower source namespace.
                if (IsWildcardParentOf(allow.FromNamespace, deny.FromNamespace) &&
                    IsWildcardParentOf(deny.ToNamespace, allow.ToNamespace))
                {
                    yield return new(
                        $"Deny rule ('from: {deny.FromNamespace}, to: {deny.ToNamespace}') is overridden by " +
                        $"allow rule ('from: {allow.FromNamespace}, to: {allow.ToNamespace}'): for dependencies " +
                        $"from '{deny.FromNamespace}' to '{allow.ToNamespace}', the allow wins because its " +
                        $"target namespace is more specific, despite the deny having a more specific source.",
                        deny.SourceLocation,
                        allow.SourceLocation);
                }
            }
        }
    }

    private static IEnumerable<RuleConflict> ValidateEqualSpecificityConflicts(DependencyRuleSet ruleSet)
    {
        // An allow and a deny whose FROM and TO pattern bases are identical both match the base
        // namespaces themselves (e.g. `System` and `System.*` both match `System`) and tie on
        // specificity, so declaration order would silently decide which one wins.
        // Identical (from, to) pairs are already reported by ValidateExactRuleConflicts.
        DependencyRule[] allows = ruleSet.Rules
            .Where(r => r.Action == DependencyAction.Allow)
            .ToArray();
        DependencyRule[] denies = ruleSet.Rules
            .Where(r => r.Action == DependencyAction.Deny)
            .ToArray();

        foreach (DependencyRule allow in allows)
        {
            foreach (DependencyRule deny in denies)
            {
                bool isSamePair = string.Equals(allow.FromNamespace, deny.FromNamespace, StringComparison.Ordinal) &&
                                  string.Equals(allow.ToNamespace, deny.ToNamespace, StringComparison.Ordinal);

                if (isSamePair ||
                    !HaveSamePatternBase(allow.FromNamespace, deny.FromNamespace) ||
                    !HaveSamePatternBase(allow.ToNamespace, deny.ToNamespace))
                {
                    continue;
                }

                yield return new(
                    $"Allow rule ('from: {allow.FromNamespace}, to: {allow.ToNamespace}') and deny rule " +
                    $"('from: {deny.FromNamespace}, to: {deny.ToNamespace}') are equally specific: for dependencies " +
                    $"from '{GetPatternBase(allow.FromNamespace)}' to '{GetPatternBase(allow.ToNamespace)}', " +
                    $"declaration order would decide which one wins.",
                    allow.SourceLocation,
                    deny.SourceLocation);
            }
        }
    }

    private static bool IsWildcardParentOf(string ancestor, string descendant)
    {
        if (!ancestor.EndsWith(".*", StringComparison.Ordinal))
        {
            return false;
        }

        string a = GetPatternBase(ancestor);
        string d = GetPatternBase(descendant);
        return a.Length == 0
            ? d.Length > 0
            : d.StartsWith(a + ".", StringComparison.Ordinal);
    }

    private static bool HaveSamePatternBase(string first, string second)
    {
        return string.Equals(GetPatternBase(first), GetPatternBase(second), StringComparison.Ordinal);
    }

    private static string GetPatternBase(string pattern)
    {
        return pattern.EndsWith(".*", StringComparison.Ordinal)
            ? pattern.Substring(0, pattern.Length - 2)
            : pattern;
    }
}
