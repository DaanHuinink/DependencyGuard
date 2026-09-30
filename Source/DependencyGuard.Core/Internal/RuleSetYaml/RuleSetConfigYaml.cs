namespace DependencyGuard.Core.Internal.RuleSetYaml;

internal sealed class RuleSetConfigYaml
{
    public RuleEntryYaml[] Allowed { get; set; } = [];
    public RuleEntryYaml[] Denied { get; set; } = [];
}

internal sealed class RuleEntryYaml
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
}
