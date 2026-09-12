using DependencyGuard.Core.Interfaces;
using YamlDotNet.RepresentationModel;

namespace DependencyGuard.Core.Internal.RuleSetYaml;

internal sealed class DependencyRuleSetParserYaml : IDependencyRuleSetParser
{
    public DependencyRuleSet Parse(string configurationText, string? sourcePath = null)
    {
        YamlStream stream = new();
        stream.Load(new StringReader(configurationText));

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            return new([], []);
        }

        List<DependencyRule> rules = [];
        ParseRuleEntries(root, "allowed", DependencyAction.Allow, sourcePath, rules);
        ParseRuleEntries(root, "denied", DependencyAction.Deny, sourcePath, rules);

        ExposedToRule[] exposedToRules = ParseExposedToRules(root, sourcePath);

        return new(rules, exposedToRules);
    }

    private static void ParseRuleEntries(YamlMappingNode root, string key, DependencyAction action,
        string? sourcePath, List<DependencyRule> results)
    {
        if (GetChild(root, key) is not YamlSequenceNode seq)
        {
            return;
        }

        foreach (YamlNode item in seq.Children)
        {
            if (item is not YamlMappingNode entry)
            {
                continue;
            }

            string from = GetScalar(entry, "from");
            string to = GetScalar(entry, "to");
            SourceLocation? location = sourcePath is not null
                ? new(sourcePath, (int)entry.Start.Line, (int)entry.Start.Column)
                : null;
            results.Add(new(from, to, action, location));
        }
    }

    private static ExposedToRule[] ParseExposedToRules(YamlMappingNode root, string? sourcePath)
    {
        if (GetChild(root, "exposedTo") is not YamlSequenceNode seq)
        {
            return [];
        }

        List<ExposedToRule> result = [];
        foreach (YamlNode item in seq.Children)
        {
            if (item is not YamlMappingNode entry)
            {
                continue;
            }

            string ns = GetScalar(entry, "namespace");
            string[] consumers = GetChild(entry, "consumers") is YamlSequenceNode consumerSeq
                ? [.. consumerSeq.Children.OfType<YamlScalarNode>().Select(s => s.Value ?? "")]
                : [];
            SourceLocation? location = sourcePath is not null
                ? new(sourcePath, (int)entry.Start.Line, (int)entry.Start.Column)
                : null;
            result.Add(new(ns, consumers, location));
        }
        return [.. result];
    }

    private static YamlNode? GetChild(YamlMappingNode mapping, string key)
    {
        return mapping.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? value) ? value : null;
    }

    private static string GetScalar(YamlMappingNode mapping, string key)
    {
        return GetChild(mapping, key) is YamlScalarNode scalar ? scalar.Value ?? "" : "";
    }
}
