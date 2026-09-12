using DependencyGuard.Core.Interfaces;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DependencyGuard.Core.Internal.RuleSetYaml;

internal sealed class DependencyRuleSetWriterYaml
{
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)
        .Build();

    public string Serialize(DependencyRuleSet ruleSet)
    {
        RuleSetConfigYaml config = new();

        DependencyRule[] allowed = ruleSet.Rules.Where(r => r.Action == DependencyAction.Allow).ToArray();
        DependencyRule[] denied = ruleSet.Rules.Where(r => r.Action == DependencyAction.Deny).ToArray();

        if (allowed.Length > 0)
        {
            config.Allowed = allowed.Select(r => new RuleEntryYaml { From = r.FromNamespace, To = r.ToNamespace }).ToList();
        }

        if (denied.Length > 0)
        {
            config.Denied = denied.Select(r => new RuleEntryYaml { From = r.FromNamespace, To = r.ToNamespace }).ToList();
        }

        if (ruleSet.ExposedToRules is { Count: > 0 })
        {
            config.ExposedTo = ruleSet.ExposedToRules
                .Select(e => new ExposedToEntryYaml { Namespace = e.Namespace, Consumers = [.. e.Consumers] })
                .ToList();
        }

        return Serializer.Serialize(config);
    }
}
