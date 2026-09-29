using DependencyGuard.Core.Interfaces;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace DependencyGuard.Core.Internal.RuleSetYaml;

// Reads a dependency-guard.yaml. Every mistake is reported (RuleSetException), because a rule that silently never
// matches is dangerous: a misspelled `denied` rule allows what it was meant to deny.
internal sealed class DependencyRuleSetParserYaml : IDependencyRuleSetParser
{
    private const string AllowedKey = "allowed";
    private const string DeniedKey = "denied";
    private const string FromKey = "from";
    private const string ToKey = "to";

    public DependencyRuleSet Parse(string configurationText, string? sourcePath = null)
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

        if (stream.Documents.Count == 0 || IsEmpty(stream.Documents[0].RootNode))
        {
            return new([]);
        }

        List<RuleSetError> errors = [];
        List<DependencyRule> rules = [];
        if (stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            errors.Add(new(
                $"A rule file is a mapping with '{AllowedKey}' and '{DeniedKey}' lists.",
                ToLocation(sourcePath, stream.Documents[0].RootNode.Start)));
            throw new RuleSetException(errors);
        }

        foreach (KeyValuePair<YamlNode, YamlNode> section in root.Children)
        {
            string key = (section.Key as YamlScalarNode)?.Value ?? string.Empty;
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

            ParseRuleEntries(section.Value, key, action.Value, sourcePath, rules, errors);
        }

        if (errors.Count > 0)
        {
            throw new RuleSetException(errors);
        }

        return new(rules);
    }

    private static void ParseRuleEntries(YamlNode section, string key, DependencyAction action, string? sourcePath,
        List<DependencyRule> rules, List<RuleSetError> errors)
    {
        // `allowed:` with nothing after it is an empty list.
        if (IsEmpty(section))
        {
            return;
        }

        if (section is not YamlSequenceNode entries)
        {
            errors.Add(new($"'{key}' is a list of rules, each with a '{FromKey}' and a '{ToKey}'.",
                ToLocation(sourcePath, section.Start)));
            return;
        }

        foreach (YamlNode item in entries.Children)
        {
            if (item is not YamlMappingNode entry)
            {
                errors.Add(new($"A rule has a '{FromKey}' and a '{ToKey}'.", ToLocation(sourcePath, item.Start)));
                continue;
            }

            int errorCount = errors.Count;
            foreach (YamlNode entryKey in entry.Children.Keys)
            {
                string name = (entryKey as YamlScalarNode)?.Value ?? string.Empty;
                if (name is not (FromKey or ToKey))
                {
                    errors.Add(new($"Unknown key '{name}' in a rule. A rule has a '{FromKey}' and a '{ToKey}'.",
                        ToLocation(sourcePath, entryKey.Start)));
                }
            }

            string? from = GetPattern(entry, FromKey, sourcePath, errors);
            string? to = GetPattern(entry, ToKey, sourcePath, errors);
            if (errors.Count == errorCount && from is not null && to is not null)
            {
                rules.Add(new(from, to, action, ToLocation(sourcePath, entry.Start)));
            }
        }
    }

    private static string? GetPattern(YamlMappingNode entry, string key, string? sourcePath, List<RuleSetError> errors)
    {
        if (!entry.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? node))
        {
            errors.Add(new($"The rule has no '{key}'.", ToLocation(sourcePath, entry.Start)));
            return null;
        }

        string pattern = (node as YamlScalarNode)?.Value?.Trim() ?? string.Empty;
        if (pattern.Length == 0)
        {
            errors.Add(new($"The rule's '{key}' is empty.", ToLocation(sourcePath, node.Start)));
            return null;
        }

        if (!IsValidPattern(pattern))
        {
            errors.Add(new(
                $"'{pattern}' is not a namespace pattern. Use a namespace ('MyApp.Api'), a namespace with everything " +
                "below it ('MyApp.Api.*') or every namespace ('.*').",
                ToLocation(sourcePath, node.Start)));
            return null;
        }

        return pattern;
    }

    // `.*`, or a dotted namespace name, optionally followed by `.*`.
    private static bool IsValidPattern(string pattern)
    {
        if (pattern == ".*")
        {
            return true;
        }

        string name = pattern.EndsWith(".*", StringComparison.Ordinal) ? pattern.Substring(0, pattern.Length - 2) : pattern;
        return name.Length > 0 && name.Split('.').All(IsIdentifier);
    }

    private static bool IsIdentifier(string segment)
    {
        string identifier = segment.StartsWith("@", StringComparison.Ordinal) ? segment.Substring(1) : segment;
        return identifier.Length > 0
               && (char.IsLetter(identifier[0]) || identifier[0] == '_')
               && identifier.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    private static bool IsEmpty(YamlNode node)
    {
        return node is YamlScalarNode { Value: null or "" } scalar && scalar.Style == ScalarStyle.Plain;
    }

    // YamlDotNet counts lines and columns from 1; a SourceLocation from 0.
    private static SourceLocation? ToLocation(string? sourcePath, Mark mark)
    {
        return sourcePath is null
            ? null
            : new(sourcePath, Math.Max(0, (int)mark.Line - 1), Math.Max(0, (int)mark.Column - 1));
    }
}
