using DependencyGuard.Core.Interfaces;

namespace DependencyGuard.Core.Internal.RuleSetYaml;

internal sealed record RuleSetParseResultYaml(IReadOnlyList<DependencyRule> Rules, IReadOnlyList<RuleSetError> Errors);
