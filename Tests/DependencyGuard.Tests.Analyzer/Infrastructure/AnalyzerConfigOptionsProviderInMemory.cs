using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyGuard.Tests.Analyzer.Infrastructure;

internal sealed class AnalyzerConfigOptionsProviderInMemory(IReadOnlyDictionary<string, string> globalOptions)
    : AnalyzerConfigOptionsProvider
{
    public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(globalOptions);

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
    {
        return Options.Empty;
    }

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
    {
        return Options.Empty;
    }

    private sealed class Options(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public static readonly Options Empty = new(new Dictionary<string, string>());

        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
        {
            return values.TryGetValue(key, out value);
        }
    }
}
