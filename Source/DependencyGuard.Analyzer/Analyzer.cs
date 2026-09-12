using System.Collections.Immutable;
using DependencyGuard.Core.Interfaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace DependencyGuard.Analyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Analyzer : DiagnosticAnalyzer
{
    public const string ConfigFileName = "dependency-guard.yaml";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
    [
        AnalyzerDiagnostics.WarningDisallowedDependency,
        AnalyzerDiagnostics.WarningConfigurationMissing,
        AnalyzerDiagnostics.ErrorConflictingRules,
        AnalyzerDiagnostics.ErrorFailedToParseYaml,
        AnalyzerDiagnostics.ErrorUnhandledException
    ];

    public override void Initialize(AnalysisContext context)
    {
        // By default, generated code is skipped
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext contextStart)
    {
        try
        {
            RegisterAnalyzer(contextStart);
        }
        catch (Exception exception)
        {
            contextStart.RegisterCompilationEndAction(contextEnd =>
                contextEnd.ReportDiagnostic(Diagnostic.Create(AnalyzerDiagnostics.ErrorUnhandledException, Location.None, exception)));
        }
    }

    private static void RegisterAnalyzer(CompilationStartAnalysisContext contextStart)
    {
        IReadOnlyList<AdditionalText> configFiles = GetConfigFiles(contextStart);
        List<DependencyRuleSet> ruleSets = CreateRuleSets(contextStart, configFiles);

        if (ruleSets.Count == 0)
        {
            contextStart.RegisterCompilationEndAction(contextEnd =>
            {
                Diagnostic configMissingDiagnostics = Diagnostic.Create(AnalyzerDiagnostics.WarningConfigurationMissing, Location.None);
                contextEnd.ReportDiagnostic(configMissingDiagnostics);
            });
            return;
        }

        IReadOnlyList<RuleConflict> conflicts = DependencyGuardFactory.TryCreateAnalyzer(
            ruleSets,
            out IDependencyAnalyzer? analyzer);

        if (conflicts.Count > 0)
        {
            contextStart.RegisterCompilationEndAction(contextEnd => ReportConflicts(conflicts, configFiles, contextEnd));
            return;
        }

        if (analyzer == null)
        {
            throw new NullReferenceException("Analyzer can't be null when there are no conflicts.");
        }

        contextStart.RegisterSyntaxNodeAction(
            ctx => AnalyzeUsingDirective(ctx, analyzer, configFiles),
            SyntaxKind.UsingDirective);

        contextStart.RegisterSyntaxNodeAction(
            ctx => AnalyzeTypeReference(ctx, analyzer, configFiles),
            SyntaxKind.IdentifierName, SyntaxKind.GenericName);

        contextStart.RegisterOperationAction(
            ctx => AnalyzeVariableDeclarator(ctx, analyzer, configFiles),
            OperationKind.VariableDeclarator);
    }

    private static List<DependencyRuleSet> CreateRuleSets(
#pragma warning disable RS1013
        CompilationStartAnalysisContext contextStart,
#pragma warning restore RS1013
        IReadOnlyList<AdditionalText> configFiles)
    {
        List<DependencyRuleSet> ruleSets = [];
        foreach (AdditionalText configFile in configFiles)
        {
            string? configText = configFile.GetText()?.ToString();
            if (configText is null)
            {
                continue;
            }

            try
            {
                DependencyRuleSet ruleset = DependencyGuardFactory.ParseFromYaml(configText, configFile.Path);
                ruleSets.Add(ruleset);
            }
            // ReSharper disable once RedundantCatchClause
            catch (Exception exception)
            {
                contextStart.RegisterCompilationEndAction(contextEnd =>
                    contextEnd.ReportDiagnostic(Diagnostic.Create(AnalyzerDiagnostics.ErrorFailedToParseYaml, Location.None, exception)));
                return ruleSets;
            }
        }

        return ruleSets;
    }

#pragma warning disable RS1012
    private static IReadOnlyList<AdditionalText> GetConfigFiles(CompilationStartAnalysisContext contextStart)
#pragma warning restore RS1012
    {
        return contextStart.Options.AdditionalFiles
            .Where(f => string.Equals(Path.GetFileName(f.Path), ConfigFileName, StringComparison.OrdinalIgnoreCase))
            .GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToArray();
    }

    private static void ReportConflicts(
        IReadOnlyList<RuleConflict> conflicts,
        IReadOnlyList<AdditionalText> configFiles,
        CompilationAnalysisContext contextEnd)
    {
        foreach (RuleConflict conflict in conflicts)
        {
            Location primary = conflict.PrimaryLocation is not null
                ? ToLocation(conflict.PrimaryLocation, configFiles)
                : Location.None;

            Location[] additional = conflict.SecondaryLocation is not null
                ? [ToLocation(conflict.SecondaryLocation, configFiles)]
                : [];

            Diagnostic conflictingRulesDiagnostic = Diagnostic.Create(AnalyzerDiagnostics.ErrorConflictingRules,
                primary,
                additional,
                null,
                conflict.Message);

            contextEnd.ReportDiagnostic(conflictingRulesDiagnostic);
        }
    }

    private static void AnalyzeUsingDirective(
        SyntaxNodeAnalysisContext context,
        IDependencyAnalyzer analyzer,
        IReadOnlyList<AdditionalText> configFiles)
    {
        UsingDirectiveSyntax usingDirective = (UsingDirectiveSyntax)context.Node;

        // Global, static and alias usings are not checked (same as the CLI).
        if (usingDirective.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword) ||
            usingDirective.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) ||
            usingDirective.Alias is not null)
        {
            return;
        }

        ISymbol? targetSymbol = context.SemanticModel.GetSymbolInfo(usingDirective.NamespaceOrType).Symbol;

        string? targetNamespace = targetSymbol switch
        {
            INamespaceSymbol { IsGlobalNamespace: false } ns => ns.ToDisplayString(),
            INamedTypeSymbol type => GetNamespaceName(type),
            _ => null,
        };

        if (targetNamespace is null)
        {
            return;
        }

        string sourceNamespace = GetSourceNamespaceForUsing(usingDirective);

        CheckAndReport(
            report: context.ReportDiagnostic,
            analyzer: analyzer,
            configFiles: configFiles,
            sourceNamespace: sourceNamespace,
            targetNamespace: targetNamespace,
            location: usingDirective.GetLocation());
    }

    private static void AnalyzeTypeReference(
        SyntaxNodeAnalysisContext context,
        IDependencyAnalyzer analyzer,
        IReadOnlyList<AdditionalText> configFiles)
    {
        // Names inside using directives are handled (or deliberately skipped) by AnalyzeUsingDirective.
        if (context.Node.FirstAncestorOrSelf<UsingDirectiveSyntax>() is not null)
        {
            return;
        }

        if (!IsNamespaceQualified(context))
        {
            return;
        }

        if (context.SemanticModel.GetSymbolInfo(context.Node).Symbol is not INamedTypeSymbol typeSymbol)
        {
            return;
        }

        string? targetNamespace = GetNamespaceName(typeSymbol);
        if (targetNamespace is null)
        {
            return;
        }

        string? sourceNamespace = GetContainingNamespace(context.Node);
        if (sourceNamespace is null)
        {
            return;
        }

        CheckAndReport(context.ReportDiagnostic, analyzer, configFiles,
            sourceNamespace, targetNamespace, context.Node.GetLocation());
    }

    private static bool IsNamespaceQualified(SyntaxNodeAnalysisContext context)
    {
        SyntaxNode node = context.Node;

        return node.Parent switch
        {
            QualifiedNameSyntax { Right: var right } qn when right == node =>
                context.SemanticModel.GetSymbolInfo(qn.Left).Symbol is INamespaceSymbol,

            MemberAccessExpressionSyntax { Name: var name } ma when name == node =>
                context.SemanticModel.GetSymbolInfo(ma.Expression).Symbol is INamespaceSymbol,

            AliasQualifiedNameSyntax { Name: var aliasName } when aliasName == node => true,

            _ => false,
        };
    }

    private static void AnalyzeVariableDeclarator(
        OperationAnalysisContext context,
        IDependencyAnalyzer analyzer,
        IReadOnlyList<AdditionalText> configFiles)
    {
        IVariableDeclaratorOperation declarator = (IVariableDeclaratorOperation)context.Operation;

        bool isInferred = declarator.Syntax switch
        {
            VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration } => declaration.Type.IsVar,
            ForEachStatementSyntax forEach => forEach.Type.IsVar,
            _ => true,
        };

        if (!isInferred)
        {
            return;
        }

        string sourceNamespace = GetNamespaceName(context.ContainingSymbol) ?? string.Empty;
        Location location = declarator.Syntax.GetLocation();

        HashSet<INamedTypeSymbol> referencedTypes = new(SymbolEqualityComparer.Default);
        CollectNamedTypes(declarator.Symbol.Type, referencedTypes);

        foreach (INamedTypeSymbol referencedType in referencedTypes)
        {
            string? targetNamespace = GetNamespaceName(referencedType);
            if (targetNamespace is null)
            {
                continue;
            }

            CheckAndReport(context.ReportDiagnostic, analyzer, configFiles,
                sourceNamespace, targetNamespace, location);
        }
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

                case INamedTypeSymbol named when named.TypeKind != TypeKind.Error:
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

    private static void CheckAndReport(
        Action<Diagnostic> report,
        IDependencyAnalyzer analyzer,
        IReadOnlyList<AdditionalText> configFiles,
        string sourceNamespace,
        string targetNamespace,
        Location location)
    {
        if (string.Equals(sourceNamespace, targetNamespace, StringComparison.Ordinal))
        {
            return;
        }

        NamespaceDependency namespaceDependency = new(sourceNamespace, targetNamespace);

        DependencyResult result = analyzer.AnalyzeDependency(namespaceDependency);
        if (result.IsAllowed)
        {
            return;
        }

        report(Diagnostic.Create(AnalyzerDiagnostics.WarningDisallowedDependency,
            location,
            GetRuleLocations(result.RuleLocation, configFiles),
            null,
            result.Reason));
    }

    private static string GetSourceNamespaceForUsing(UsingDirectiveSyntax usingDirective)
    {
        for (SyntaxNode? current = usingDirective.Parent; current is not null; current = current.Parent)
        {
            if (current is BaseNamespaceDeclarationSyntax ns)
            {
                return ns.Name.ToString();
            }
        }

        if (usingDirective.SyntaxTree.GetRoot() is CompilationUnitSyntax compilationUnit)
        {
            BaseNamespaceDeclarationSyntax? fileNamespace = compilationUnit.Members
                .OfType<BaseNamespaceDeclarationSyntax>()
                .FirstOrDefault();

            if (fileNamespace is not null)
            {
                return fileNamespace.Name.ToString();
            }
        }

        return string.Empty;
    }

    private static string? GetNamespaceName(ISymbol symbol)
    {
        INamespaceSymbol? ns = symbol as INamespaceSymbol ?? symbol.ContainingNamespace;
        return ns is null || ns.IsGlobalNamespace ? null : ns.ToDisplayString();
    }

    private static Location[] GetRuleLocations(SourceLocation? ruleLoc, IReadOnlyList<AdditionalText> configFiles)
    {
        return ruleLoc is not null
            ? [ToLocation(ruleLoc, configFiles)]
            : [];
    }

    private static Location ToLocation(SourceLocation loc, IReadOnlyList<AdditionalText> configFiles)
    {
        AdditionalText? configFile = configFiles
            .FirstOrDefault(f => string.Equals(f.Path, loc.FilePath, StringComparison.OrdinalIgnoreCase));

        if (configFile is null)
        {
            return Location.None;
        }

        SourceText? text = configFile.GetText();
        if (text is null || loc.Line >= text.Lines.Count)
        {
            return Location.None;
        }

        int start = text.Lines[loc.Line].Start + loc.Column;
        TextSpan span = new(start, 0);
        LinePositionSpan lineSpan = new(new(loc.Line, loc.Column), new(loc.Line, loc.Column));

        return Location.Create(configFile.Path, span, lineSpan);
    }

    private static string? GetContainingNamespace(SyntaxNode node)
    {
        SyntaxNode? current = node.Parent;
        while (current is not null)
        {
            if (current is BaseNamespaceDeclarationSyntax ns)
            {
                return ns.Name.ToString();
            }

            current = current.Parent;
        }

        if (node.SyntaxTree.GetRoot() is CompilationUnitSyntax compilationUnit)
        {
            return compilationUnit.Members
                .OfType<FileScopedNamespaceDeclarationSyntax>()
                .FirstOrDefault()
                ?.Name.ToString();
        }

        return null;
    }
}
