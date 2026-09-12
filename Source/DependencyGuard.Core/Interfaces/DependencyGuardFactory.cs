using DependencyGuard.Core.Internal.Analyzer;
using DependencyGuard.Core.Internal.RuleSetYaml;
using DependencyGuard.Core.Internal.Validation;

namespace DependencyGuard.Core.Interfaces;

public static class DependencyGuardFactory
{
    public static DependencyRuleSet ParseFromYaml(string yaml, string? sourcePath = null)
    {
        DependencyRuleSetParserYaml parser = new();
        return parser.Parse(yaml, sourcePath);
    }

    public static IReadOnlyList<RuleConflict> TryCreateAnalyzer(
        IReadOnlyCollection<DependencyRuleSet> ruleSets,
        out IDependencyAnalyzer? analyzer)
    {
        DependencyRuleSet mergedRuleset = MergeRulesets(ruleSets);
        IReadOnlyList<RuleConflict> conflicts = Validate(mergedRuleset);

        if (conflicts.Count > 0)
        {
            analyzer = null;
        }
        else
        {
            analyzer = new DependencyAnalyzer(mergedRuleset);
        }

        return conflicts;
    }

    private static IReadOnlyList<RuleConflict> Validate(DependencyRuleSet ruleSet)
    {
        RuleSetValidator validator = new();
        return validator.Validate(ruleSet);
    }

    public static string SerializeToYaml(DependencyRuleSet ruleSet)
    {
        DependencyRuleSetWriterYaml writer = new();
        return writer.Serialize(ruleSet);
    }

    private static DependencyRuleSet MergeRulesets(IReadOnlyCollection<DependencyRuleSet> ruleSets)
    {
        if (ruleSets.Count == 0)
        {
            throw new ArgumentException("At least one ruleset must be provided.", nameof(ruleSets));
        }

        if (ruleSets.Count == 1)
        {
            return ruleSets.First();
        }

        DependencyRule[] rules = ruleSets
            .SelectMany(r => r.Rules)
            .ToArray();

        ExposedToRule[] exposedTo = ruleSets
            .SelectMany(r => r.ExposedToRules ?? [])
            .ToArray();

        return new(rules, exposedTo);
    }
}
