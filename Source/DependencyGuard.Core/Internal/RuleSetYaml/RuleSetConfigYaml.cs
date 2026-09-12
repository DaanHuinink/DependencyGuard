namespace DependencyGuard.Core.Internal.RuleSetYaml;

internal sealed class RuleSetConfigYaml
{
    public List<RuleEntryYaml>? Allowed { get; set; }
    public List<RuleEntryYaml>? Denied { get; set; }
    public List<ExposedToEntryYaml>? ExposedTo { get; set; }
}

internal sealed class RuleEntryYaml
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
}

internal sealed class ExposedToEntryYaml
{
    public string Namespace { get; set; } = "";
    public List<string> Consumers { get; set; } = [];
}
