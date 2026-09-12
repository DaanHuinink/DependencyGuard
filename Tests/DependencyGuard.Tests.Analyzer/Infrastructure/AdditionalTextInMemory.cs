using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DependencyGuard.Tests.Analyzer.Infrastructure;

internal sealed class AdditionalTextInMemory(string path, string text) : AdditionalText
{
    public override string Path { get; } = path;

    public override SourceText GetText(CancellationToken cancellationToken = default)
    {
        return SourceText.From(text);
    }
}
