using System.Collections.Concurrent;
using DependencyGuard.Core.Interfaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace DependencyGuard.Analyzer;

// The checks for one compilation. A dependency is a name in the code that refers to a type in another namespace,
// however that name came into scope: a using, a global or implicit using, an alias, a static using, an extension
// method or simply the enclosing namespace. A plain `using Namespace;` is checked as well, where it is written.
internal sealed class AnalyzerCompilation(
    IDependencyAnalyzer analyzer,
    IReadOnlyList<AdditionalText> configFiles,
    string rootNamespace,
    IAssemblySymbol assembly)
{
    // Razor copies the usings of _Imports.razor into every component: each of those lines is reported once.
    private readonly ConcurrentDictionary<string, bool> _reportedMappedUsings = new(StringComparer.Ordinal);

    public void AnalyzeUsingDirective(SyntaxNodeAnalysisContext context)
    {
        UsingDirectiveSyntax usingDirective = (UsingDirectiveSyntax)context.Node;
        Location location = usingDirective.GetLocation();
        if (context.IsGeneratedCode && !IsWrittenByHand(location))
        {
            return;
        }

        // What a global, static or alias using brings in is checked where it is used (AnalyzeName).
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

    // The types a name makes the code depend on: none for a local, a parameter, a namespace or an instance member.
    private static IReadOnlyList<INamedTypeSymbol> GetReferencedTypes(SimpleNameSyntax name, SemanticModel model, ISymbol? containingSymbol)
    {
        ISymbol? symbol = model.GetSymbolInfo(name).Symbol;
        if (symbol is null)
        {
            return [];
        }

        // `var` stands for the type the compiler infers: that type and the types in it (List<Order>: List and Order).
        if (name is IdentifierNameSyntax { IsVar: true } && symbol is ITypeSymbol inferred)
        {
            HashSet<INamedTypeSymbol> inferredTypes = new(SymbolEqualityComparer.Default);
            CollectNamedTypes(inferred, inferredTypes);
            return inferredTypes.ToArray();
        }

        // In Outer.Inner or Type.Member the dependency is the name on the left, which is checked by itself.
        if (IsQualifiedByType(name, model))
        {
            return [];
        }

        switch (symbol)
        {
            case INamedTypeSymbol { TypeKind: not TypeKind.Error } type:
                return [type];

            // The name of an attribute refers to its constructor.
            case IMethodSymbol { MethodKind: MethodKind.Constructor } constructor:
                return [constructor.ContainingType];

            case IMethodSymbol or IPropertySymbol or IFieldSymbol or IEventSymbol when symbol.ContainingType is { } owner:
                return IsImported(symbol, owner, containingSymbol) ? [owner] : [];

            default:
                return [];
        }
    }

    // A member used without its type's name is a dependency on that type when it came in through a static using, or
    // when it extends a value: an extension method, or a member of a C# 14 extension block. An instance member is not,
    // and neither is a static member of this type, an outer one or a base class, which are in scope anyway.
    private static bool IsImported(ISymbol member, INamedTypeSymbol owner, ISymbol? containingSymbol)
    {
        if (member is IMethodSymbol { ReducedFrom: not null } || owner.IsStatic || IsExtensionBlock(owner))
        {
            return true;
        }

        return member.IsStatic && !IsEnclosingOrBaseType(owner, containingSymbol);
    }

    // A C# 14 extension block is a type of its own kind inside a static class (TypeKind.Extension, which is newer than
    // the Roslyn this analyzer is built against).
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

    // A using directive belongs to the namespace it is written in. One above all namespaces (the usual place next to a
    // file-scoped namespace) serves the whole file: every namespace in it, and the root namespace for code outside them.
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
            namespaces.Add(rootNamespace);
        }

        return namespaces.Distinct(StringComparer.Ordinal);
    }

    // The namespace code belongs to; code in no namespace belongs to the project's root namespace.
    private string GetNamespace(ISymbol? symbol)
    {
        INamespaceSymbol? ns = symbol as INamespaceSymbol ?? symbol?.ContainingNamespace;
        return ns is null || ns.IsGlobalNamespace ? rootNamespace : ns.ToDisplayString();
    }

    // A type in no namespace belongs to its project: this project's root namespace, or else its assembly's name.
    private string GetNamespace(INamedTypeSymbol type)
    {
        if (type.ContainingNamespace is { IsGlobalNamespace: false } ns)
        {
            return ns.ToDisplayString();
        }

        return type.ContainingAssembly is null || SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, assembly)
            ? rootNamespace
            : type.ContainingAssembly.Name;
    }

    // Generated code is only checked where a #line directive maps it back to a file someone wrote, such as the C# that
    // Razor makes of what is written in a .razor file.
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

        DependencyResult result = analyzer.AnalyzeDependency(new(sourceNamespace, targetNamespace));
        if (result.IsAllowed)
        {
            return;
        }

        Location[] ruleLocations = result.RuleLocation is not null
            ? [Analyzer.ToLocation(result.RuleLocation, configFiles)]
            : [];

        report(Diagnostic.Create(AnalyzerDiagnostics.WarningDisallowedDependency, location, ruleLocations, null, result.Reason));
    }
}
