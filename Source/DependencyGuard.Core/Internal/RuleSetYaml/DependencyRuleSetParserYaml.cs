using DependencyGuard.Core.Interfaces;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace DependencyGuard.Core.Internal.RuleSetYaml;

internal sealed class DependencyRuleSetParserYaml : IDependencyRuleSetParser
{
    private const string AllowedKey = "allowed";
    private const string DeniedKey = "denied";
    private const string FromKey = "from";
    private const string ToKey = "to";

    public DependencyRuleSet Parse(string configurationText, string? sourcePath = null)
    {
        YamlStream stream = GetYamlStream(configurationText, sourcePath);
        if (IsStreamEmpty(stream))
        {
            return new([]);
        }

        YamlMappingNode root = GetRoot(sourcePath, stream);
        List<DependencyRule> rules = ParseRules(sourcePath, root);
        return new(rules);
    }

    private static YamlMappingNode GetRoot(string? sourcePath, YamlStream stream)
    {
        if (stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw new RuleSetException([
                new(
                    $"A rule file is a mapping with '{AllowedKey}' and '{DeniedKey}' lists.",
                    ToLocation(sourcePath, stream.Documents[0].RootNode.Start))
            ]);
        }

        return root;
    }

    private static bool IsStreamEmpty(YamlStream stream)
    {
        return stream.Documents.Count == 0 || IsEmpty(stream.Documents[0].RootNode);
    }

    private static YamlStream GetYamlStream(string configurationText, string? sourcePath)
    {
        YamlStream stream = new();
        try
        {
            stream.Load(new StringReader(configurationText));
        }
        catch (YamlException exception)
        {
            throw new RuleSetException([new(exception.Message, ToLocation(sourcePath, exception.Start))]);
        }

        return stream;
    }

    private static List<DependencyRule> ParseRules(string? sourcePath, YamlMappingNode root)
    {
        List<DependencyRule> rules = [];
        List<RuleSetError> errors = [];

        foreach (KeyValuePair<YamlNode, YamlNode> section in root.Children)
        {
            string key = GetScalarValue(section.Key);
            DependencyAction? action = key switch
            {
                AllowedKey => DependencyAction.Allow,
                DeniedKey => DependencyAction.Deny,
                _ => null,
            };

            if (action is null)
            {
                errors.Add(new(
                    $"Unknown key '{key}'. A rule file has '{AllowedKey}' and '{DeniedKey}'.",
                    ToLocation(sourcePath, section.Key.Start)));
                continue;
            }

            RuleSetParseResultYaml result = ParseRuleEntries(section.Value, key, action.Value, sourcePath);
            rules.AddRange(result.Rules);
            errors.AddRange(result.Errors);
        }

        if (errors.Count > 0)
        {
            throw new RuleSetException(errors);
        }

        return rules;
    }

    private static RuleSetParseResultYaml ParseRuleEntries(
            YamlNode section,
            string key,
            DependencyAction action,
            string? sourcePath)
    {
        if (IsEmpty(section) || section is YamlSequenceNode { Children.Count: 0 })
        {
            return new([], [new($"'{key}' has no rules.", ToLocation(sourcePath, section.Start))]);
        }

        if (section is not YamlSequenceNode entries)
        {
            return new(
                [],
                [
                    new(
                        $"'{key}' is a list of rules, each with a '{FromKey}' and a '{ToKey}'.",
                        ToLocation(sourcePath, section.Start))
                ]);
        }

        RuleSetParseResultYaml[] results = entries.Children
            .Select(item => ParseRuleEntry(item, action, sourcePath))
            .ToArray();
        return new(
            results.SelectMany(result => result.Rules).ToArray(),
            results.SelectMany(result => result.Errors).ToArray());
    }

    private static RuleSetParseResultYaml ParseRuleEntry(YamlNode item, DependencyAction action, string? sourcePath)
    {
        if (item is not YamlMappingNode entry)
        {
            return new([], [new($"A rule has a '{FromKey}' and a '{ToKey}'.", ToLocation(sourcePath, item.Start))]);
        }

        RuleSetError[] errors = GetUnknownKeyErrors(entry, sourcePath)
            .Concat(GetPatternErrors(entry, FromKey, sourcePath))
            .Concat(GetPatternErrors(entry, ToKey, sourcePath))
            .ToArray();
        if (errors.Length > 0)
        {
            return new([], errors);
        }

        DependencyRule rule = new(
            GetPattern(entry, FromKey),
            GetPattern(entry, ToKey),
            action,
            ToLocation(sourcePath, entry.Start));
        return new([rule], []);
    }

    private static IEnumerable<RuleSetError> GetUnknownKeyErrors(YamlMappingNode entry, string? sourcePath)
    {
        return entry.Children.Keys
            .Where(key => GetScalarValue(key) is not (FromKey or ToKey))
            .Select(key => new RuleSetError(
                $"Unknown key '{GetScalarValue(key)}' in a rule. A rule has a '{FromKey}' and a '{ToKey}'.",
                ToLocation(sourcePath, key.Start)));
    }

    private static IEnumerable<RuleSetError> GetPatternErrors(YamlMappingNode entry, string key, string? sourcePath)
    {
        if (!entry.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? node))
        {
            return [new($"The rule has no '{key}'.", ToLocation(sourcePath, entry.Start))];
        }

        string pattern = GetScalarValue(node).Trim();
        if (pattern.Length == 0)
        {
            return [new($"The rule's '{key}' is empty.", ToLocation(sourcePath, node.Start))];
        }

        if (!IsValidPattern(pattern))
        {
            return [
                new(
                    $"'{pattern}' is not a namespace pattern. Use a namespace ('MyApp.Api'), a namespace with " +
                    "everything below it ('MyApp.Api.*') or every namespace ('.*').",
                    ToLocation(sourcePath, node.Start))
            ];
        }

        return [];
    }

    private static string GetPattern(YamlMappingNode entry, string key)
    {
        return GetScalarValue(entry.Children[new YamlScalarNode(key)]).Trim();
    }

    private static string GetScalarValue(YamlNode node)
    {
        return (node as YamlScalarNode)?.Value ?? string.Empty;
    }

    private static bool IsValidPattern(string pattern)
    {
        if (pattern == ".*")
        {
            return true;
        }

        string name = pattern.EndsWith(".*", StringComparison.Ordinal)
            ? pattern.Substring(0, pattern.Length - 2)
            : pattern;
        return name.Length > 0 && name.Split('.').All(IsIdentifier);
    }

    private static bool IsIdentifier(string segment)
    {
        string identifier = segment.StartsWith("@", StringComparison.Ordinal)
            ? segment.Substring(1)
            : segment;
        return identifier.Length > 0
               && (char.IsLetter(identifier[0]) || identifier[0] == '_')
               && identifier.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    private static bool IsEmpty(YamlNode node)
    {
        return node is YamlScalarNode { Value: null or "" } scalar && scalar.Style == ScalarStyle.Plain;
    }

    private static SourceLocation? ToLocation(string? sourcePath, Mark mark)
    {
        return sourcePath is null
            ? null
            : new(sourcePath, Math.Max(0, (int)mark.Line - 1), Math.Max(0, (int)mark.Column - 1));
    }
}
