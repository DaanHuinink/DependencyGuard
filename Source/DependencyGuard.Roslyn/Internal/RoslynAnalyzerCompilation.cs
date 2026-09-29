using System.Collections.Concurrent;
using System.Collections.Immutable;
using DependencyGuard.Core.Interfaces;
using DependencyGuard.Roslyn.Interfaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace DependencyGuard.Roslyn.Internal;

internal sealed class RoslynAnalyzerCompilation
{
    private readonly IDependencyAnalyzer _analyzer;
    private readonly IReadOnlyList<AdditionalText> _configFiles;
    private readonly string _rootNamespace;
    private readonly IAssemblySymbol _assembly;
    private readonly ConcurrentDictionary<string, bool> _reportedMappedUsings = new(StringComparer.Ordinal);

    public RoslynAnalyzerCompilation(
        IDependencyAnalyzer analyzer,
        IReadOnlyList<AdditionalText> configFiles,
        string rootNamespace,
        IAssemblySymbol assembly)
    {
        _analyzer = analyzer;
        _configFiles = configFiles;
        _rootNamespace = rootNamespace;
        _assembly = assembly;
    }

    public void AnalyzeUsingDirective(SyntaxNodeAnalysisContext context)
    {
        UsingDirectiveSyntax usingDirective = (UsingDirectiveSyntax)context.Node;
        Location location = usingDirective.GetLocation();
        if (context.IsGeneratedCode && !IsWrittenByHand(location))
        {
            return;
        }

        if (usingDirective.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword) ||
            usingDirective.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) ||
            usingDirective.Alias is not null)
        {
            return;
        }

        if (context.SemanticModel.GetSymbolInfo(usingDirective.NamespaceOrType).Symbol is not INamespaceSymbol { IsGlobalNamespace: false } target)
        {
            return;
        }

        string targetNamespace = target.ToDisplayString();
        foreach (string sourceNamespace in GetUsingSourceNamespaces(usingDirective, context.SemanticModel))
        {
            if (context.IsGeneratedCode && !IsFirstReport(location, sourceNamespace, targetNamespace))
            {
                continue;
            }

            CheckAndReport(context.ReportDiagnostic, sourceNamespace, targetNamespace, location);
        }
    }

    public void AnalyzeName(SyntaxNodeAnalysisContext context)
    {
        SimpleNameSyntax name = (SimpleNameSyntax)context.Node;
        if (name.FirstAncestorOrSelf<UsingDirectiveSyntax>() is not null || IsNamespaceDeclarationName(name))
        {
            return;
        }

        Location location = name.GetLocation();
        if (context.IsGeneratedCode && !IsWrittenByHand(location))
        {
            return;
        }

        IEnumerable<string> targetNamespaces = GetReferencedTypes(name, context.SemanticModel, context.ContainingSymbol)
            .Select(GetNamespace)
            .Distinct(StringComparer.Ordinal);

        string sourceNamespace = GetNamespace(context.ContainingSymbol);
        foreach (string targetNamespace in targetNamespaces)
        {
            CheckAndReport(context.ReportDiagnostic, sourceNamespace, targetNamespace, location);
        }
    }

    private static IReadOnlyList<INamedTypeSymbol> GetReferencedTypes(SimpleNameSyntax name, SemanticModel model, ISymbol? containingSymbol)
    {
        ISymbol? symbol = model.GetSymbolInfo(name).Symbol;
        if (symbol is null)
        {
            return [];
        }

        if (name is IdentifierNameSyntax { IsVar: true } && symbol is ITypeSymbol inferred)
        {
            HashSet<INamedTypeSymbol> inferredTypes = new(SymbolEqualityComparer.Default);
            CollectNamedTypes(inferred, inferredTypes);
            return inferredTypes.ToArray();
        }

        if (IsQualifiedByType(name, model))
        {
            return [];
        }

        switch (symbol)
        {
            case INamedTypeSymbol { TypeKind: not TypeKind.Error } type:
                return [type];

            case IMethodSymbol { MethodKind: MethodKind.Constructor } constructor:
                return [constructor.ContainingType];

            case IMethodSymbol or IPropertySymbol or IFieldSymbol or IEventSymbol when symbol.ContainingType is { } owner:
                return IsImported(symbol, owner, containingSymbol)
                    ? [owner]
                    : [];

            default:
                return [];
        }
    }

    private static bool IsImported(ISymbol member, INamedTypeSymbol owner, ISymbol? containingSymbol)
    {
        if (member is IMethodSymbol { ReducedFrom: not null } || owner.IsStatic || IsExtensionBlock(owner))
        {
            return true;
        }

        return member.IsStatic && !IsEnclosingOrBaseType(owner, containingSymbol);
    }

    // TypeKind.Extension is newer than the Roslyn version this library builds against
    private static bool IsExtensionBlock(INamedTypeSymbol type)
    {
        return type.ContainingType is { IsStatic: true }
               && type.TypeKind is not (TypeKind.Class or TypeKind.Struct or TypeKind.Interface or TypeKind.Enum or TypeKind.Delegate);
    }

    private static bool IsQualifiedByType(SimpleNameSyntax name, SemanticModel model)
    {
        ExpressionSyntax? qualifier = name.Parent switch
        {
            QualifiedNameSyntax qualified when qualified.Right == name => qualified.Left,
            MemberAccessExpressionSyntax memberAccess when memberAccess.Name == name => memberAccess.Expression,
            _ => null,
        };

        return qualifier is not null && model.GetSymbolInfo(qualifier).Symbol is ITypeSymbol;
    }

    private static bool IsEnclosingOrBaseType(INamedTypeSymbol owner, ISymbol? containingSymbol)
    {
        INamedTypeSymbol? enclosing = containingSymbol as INamedTypeSymbol ?? containingSymbol?.ContainingType;
        for (INamedTypeSymbol? type = enclosing; type is not null; type = type.ContainingType)
        {
            for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, owner.OriginalDefinition))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void CollectNamedTypes(ITypeSymbol? type, HashSet<INamedTypeSymbol> result)
    {
        while (true)
        {
            switch (type)
            {
                case IArrayTypeSymbol array:
                {
                    type = array.ElementType;
                    continue;
                }

                case IPointerTypeSymbol pointer:
                {
                    type = pointer.PointedAtType;
                    continue;
                }

                case INamedTypeSymbol named when named.TypeKind != TypeKind.Error && !named.IsAnonymousType:
                {
                    if (result.Add(named))
                    {
                        foreach (ITypeSymbol typeArgument in named.TypeArguments)
                        {
                            CollectNamedTypes(typeArgument, result);
                        }
                    }

                    return;
                }
            }

            break;
        }
    }

    private static bool IsNamespaceDeclarationName(SimpleNameSyntax name)
    {
        return name.FirstAncestorOrSelf<BaseNamespaceDeclarationSyntax>() is { } declaration
               && declaration.Name.Span.Contains(name.Span);
    }

    private IEnumerable<string> GetUsingSourceNamespaces(UsingDirectiveSyntax usingDirective, SemanticModel model)
    {
        if (usingDirective.Parent is BaseNamespaceDeclarationSyntax declaration)
        {
            return [GetNamespace(model.GetDeclaredSymbol(declaration))];
        }

        CompilationUnitSyntax file = (CompilationUnitSyntax)usingDirective.Parent!;
        List<string> namespaces = file.Members
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Select(ns => GetNamespace(model.GetDeclaredSymbol(ns)))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (namespaces.Count == 0 || file.Members.Any(member => member is not BaseNamespaceDeclarationSyntax))
        {
            namespaces.Add(_rootNamespace);
        }

        return namespaces.Distinct(StringComparer.Ordinal);
    }

    private string GetNamespace(ISymbol? symbol)
    {
        INamespaceSymbol? ns = symbol as INamespaceSymbol ?? symbol?.ContainingNamespace;
        return ns is null || ns.IsGlobalNamespace
            ? _rootNamespace
            : ns.ToDisplayString();
    }

    private string GetNamespace(INamedTypeSymbol type)
    {
        if (type.ContainingNamespace is { IsGlobalNamespace: false } ns)
        {
            return ns.ToDisplayString();
        }

        return type.ContainingAssembly is null || SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, _assembly)
            ? _rootNamespace
            : type.ContainingAssembly.Name;
    }

    private static bool IsWrittenByHand(Location location)
    {
        return location.GetMappedLineSpan().HasMappedPath
               && location.SourceTree is { } tree
               && tree.GetLineVisibility(location.SourceSpan.Start) == LineVisibility.Visible;
    }

    private bool IsFirstReport(Location location, string sourceNamespace, string targetNamespace)
    {
        FileLinePositionSpan mapped = location.GetMappedLineSpan();
        string key = $"{mapped.Path}|{mapped.StartLinePosition.Line}|{sourceNamespace}|{targetNamespace}";
        return _reportedMappedUsings.TryAdd(key, true);
    }

    private void CheckAndReport(Action<Diagnostic> report, string sourceNamespace, string targetNamespace, Location location)
    {
        if (string.Equals(sourceNamespace, targetNamespace, StringComparison.Ordinal))
        {
            return;
        }

        DependencyResult result = _analyzer.AnalyzeDependency(new(sourceNamespace, targetNamespace));
        if (result.IsAllowed)
        {
            return;
        }

        Location[] ruleLocations = result.RuleLocation is not null
            ? [RoslynAnalyzerRuleFiles.ToLocation(result.RuleLocation, _configFiles)]
            : [];

        ImmutableDictionary<string, string?> properties = ImmutableDictionary<string, string?>.Empty
            .Add(RoslynAnalyzerContract.SourceNamespaceProperty, sourceNamespace)
            .Add(RoslynAnalyzerContract.TargetNamespaceProperty, targetNamespace);

        report(Diagnostic.Create(RoslynAnalyzerDiagnostics.WarningDisallowedDependency, location, ruleLocations, properties, result.Reason));
    }
}
