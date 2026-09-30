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
        RuleSetConfigYaml config = new()
        {
            Allowed = GetEntries(ruleSet, DependencyAction.Allow),
            Denied = GetEntries(ruleSet, DependencyAction.Deny),
        };

        return Serializer.Serialize(config);
    }

    private static RuleEntryYaml[] GetEntries(DependencyRuleSet ruleSet, DependencyAction action)
    {
        return ruleSet.Rules
            .Where(r => r.Action == action)
            .Select(r => new RuleEntryYaml { From = r.FromNamespace, To = r.ToNamespace })
            .ToArray();
    }
}
