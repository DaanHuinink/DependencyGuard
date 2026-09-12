using DependencyGuard.Core.Interfaces;

namespace DependencyGuard.Core.Internal.Validation;

internal sealed class RuleSetValidator
{
    public IReadOnlyList<RuleConflict> Validate(DependencyRuleSet ruleSet)
    {
        List<RuleConflict> conflicts = [];

        ValidateExactRuleConflicts(ruleSet, conflicts);
        ValidateCrossingSpecificityConflicts(ruleSet, conflicts);
        ValidateEqualSpecificityConflicts(ruleSet, conflicts);
        ValidateExposedToConflicts(ruleSet, conflicts);

        return conflicts;
    }

    private static void ValidateExactRuleConflicts(DependencyRuleSet ruleSet, List<RuleConflict> conflicts)
    {
        foreach (IGrouping<(string, string), DependencyRule> group in ruleSet.Rules
            .GroupBy(r => (r.FromNamespace, r.ToNamespace))
            .Where(g => g.Select(r => r.Action).Distinct().Count() > 1))
        {
            DependencyRule allow = group.First(r => r.Action == DependencyAction.Allow);
            DependencyRule deny = group.First(r => r.Action == DependencyAction.Deny);
            string msg = $"Rule for '{group.Key.Item1}' → '{group.Key.Item2}' is declared as both allowed and denied.";
            conflicts.Add(new(msg, allow.SourceLocation, deny.SourceLocation));
        }
    }

    private static void ValidateCrossingSpecificityConflicts(DependencyRuleSet ruleSet, List<RuleConflict> conflicts)
    {
        // Rules are ranked by TO length first, then FROM length. When an allow and a deny
        // have "crossed" specificity (one is more specific in FROM, the other in TO), the
        // TO-wins ordering produces unintuitive results for the intersection of their ranges.
        DependencyRule[] allows = ruleSet.Rules.Where(r => r.Action == DependencyAction.Allow).ToArray();
        DependencyRule[] denies = ruleSet.Rules.Where(r => r.Action == DependencyAction.Deny).ToArray();

        foreach (DependencyRule allow in allows)
        {
            foreach (DependencyRule deny in denies)
            {
                // Case A: allow FROM is more specific (child of deny FROM),
                //         deny TO is more specific (child of allow TO).
                //         → deny wins for (allow.FROM → deny.TO) even though allow explicitly
                //           targets the narrower source namespace.
                if (PatternBaseIsStrictPrefix(deny.FromNamespace, allow.FromNamespace) &&
                    PatternBaseIsStrictPrefix(allow.ToNamespace, deny.ToNamespace))
                {
                    conflicts.Add(new(
                        $"Allow rule ('from: {allow.FromNamespace}, to: {allow.ToNamespace}') is overridden by " +
                        $"deny rule ('from: {deny.FromNamespace}, to: {deny.ToNamespace}'): for dependencies " +
                        $"from '{allow.FromNamespace}' to '{deny.ToNamespace}', the deny wins because its " +
                        $"target namespace is more specific, despite the allow having a more specific source.",
                        allow.SourceLocation,
                        deny.SourceLocation));
                }

                // Case B: deny FROM is more specific (child of allow FROM),
                //         allow TO is more specific (child of deny TO).
                //         → allow wins for (deny.FROM → allow.TO) even though deny explicitly
                //           targets the narrower source namespace.
                if (PatternBaseIsStrictPrefix(allow.FromNamespace, deny.FromNamespace) &&
                    PatternBaseIsStrictPrefix(deny.ToNamespace, allow.ToNamespace))
                {
                    conflicts.Add(new(
                        $"Deny rule ('from: {deny.FromNamespace}, to: {deny.ToNamespace}') is overridden by " +
                        $"allow rule ('from: {allow.FromNamespace}, to: {allow.ToNamespace}'): for dependencies " +
                        $"from '{deny.FromNamespace}' to '{allow.ToNamespace}', the allow wins because its " +
                        $"target namespace is more specific, despite the deny having a more specific source.",
                        deny.SourceLocation,
                        allow.SourceLocation));
                }
            }
        }
    }

    private static void ValidateEqualSpecificityConflicts(DependencyRuleSet ruleSet, List<RuleConflict> conflicts)
    {
        // An allow and a deny whose FROM and TO pattern bases are identical both match the base
        // namespaces themselves (e.g. `System` and `System.*` both match `System`) and tie on
        // specificity, so declaration order would silently decide which one wins.
        // Identical (from, to) pairs are already reported by ValidateExactRuleConflicts.
        DependencyRule[] allows = ruleSet.Rules.Where(r => r.Action == DependencyAction.Allow).ToArray();
        DependencyRule[] denies = ruleSet.Rules.Where(r => r.Action == DependencyAction.Deny).ToArray();

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

                conflicts.Add(new(
                    $"Allow rule ('from: {allow.FromNamespace}, to: {allow.ToNamespace}') and deny rule " +
                    $"('from: {deny.FromNamespace}, to: {deny.ToNamespace}') are equally specific: for dependencies " +
                    $"from '{GetPatternBase(allow.FromNamespace)}' to '{GetPatternBase(allow.ToNamespace)}', " +
                    $"declaration order would decide which one wins.",
                    allow.SourceLocation,
                    deny.SourceLocation));
            }
        }
    }

    private static void ValidateExposedToConflicts(DependencyRuleSet ruleSet, List<RuleConflict> conflicts)
    {
        foreach (IGrouping<string, ExposedToRule> group in (ruleSet.ExposedToRules ?? [])
            .GroupBy(e => e.Namespace, StringComparer.Ordinal)
            .Where(g => g.Count() > 1))
        {
            ExposedToRule first = group.First();
            ExposedToRule second = group.Skip(1).First();
            conflicts.Add(new(
                $"Namespace '{group.Key}' has multiple exposedTo entries. Merge them into a single entry.",
                first.SourceLocation,
                second.SourceLocation));
        }
    }

    // Compares pattern bases (stripping `.*` suffix) so wildcard patterns are treated correctly.
    private static bool PatternBaseIsStrictPrefix(string ancestor, string descendant)
    {
        string a = GetPatternBase(ancestor);
        string d = GetPatternBase(descendant);
        return d.StartsWith(a + ".", StringComparison.Ordinal);
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
