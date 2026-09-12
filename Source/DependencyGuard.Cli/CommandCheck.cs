using DependencyGuard.Core.Interfaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyGuard.Cli;

internal static class CommandCheck
{
    private const string DefaultConfigFileName = "dependency-guard.yaml";

    internal static int Run(string[] args)
    {
        if (!TryParseArgs(args, out string? globalConfigPath, out string targetPath, out string? error) ||
            !Program.TryResolveProjectFiles(targetPath, out List<string> projectFiles, out error))
        {
            Program.PrintError(error);
            return 2;
        }

        int totalViolations = 0;
        int invalidConfigs = 0;
        foreach (string projectFile in projectFiles)
        {
            if (TryAnalyzeProject(projectFile, globalConfigPath, out int violations))
            {
                totalViolations += violations;
            }
            else
            {
                invalidConfigs++;
            }
        }

        Console.WriteLine();
        if (invalidConfigs > 0)
        {
            Program.PrintError($"Found {invalidConfigs} project(s) with an invalid configuration.");
        }

        if (totalViolations > 0)
        {
            Program.PrintWarning($"Found {totalViolations} violation(s).");
        }

        if (invalidConfigs > 0 || totalViolations > 0)
        {
            return 1;
        }

        Program.PrintSuccess("No violations found.");
        return 0;
    }

    private static bool TryParseArgs(string[] args, out string? globalConfigPath, out string targetPath, out string? error)
    {
        globalConfigPath = null;
        targetPath = Directory.GetCurrentDirectory();
        error = null;

        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "--config" || args[i] == "-c") && i + 1 < args.Length)
            {
                globalConfigPath = Path.GetFullPath(args[++i]);
            }
            else
            {
                targetPath = Path.GetFullPath(args[i]);
            }
        }

        if (globalConfigPath is not null && !File.Exists(globalConfigPath))
        {
            error = $"Config file not found: {globalConfigPath}";
            return false;
        }

        return true;
    }

    // Returns false when the project's configuration can't be parsed or contains conflicting rules.
    private static bool TryAnalyzeProject(string projectFile, string? globalConfigPath, out int violations)
    {
        violations = 0;
        string projectDir = Path.GetDirectoryName(projectFile)!;
        string projectName = Path.GetFileNameWithoutExtension(projectFile);
        string configPath = globalConfigPath ?? Path.Combine(projectDir, DefaultConfigFileName);

        if (!File.Exists(configPath))
        {
            Console.WriteLine($"[skip] {projectName}: no {DefaultConfigFileName} found");
            return true;
        }

        if (!TryLoadAnalyzer(configPath, projectName, out IDependencyAnalyzer? analyzer))
        {
            return false;
        }

        string[] sourceFiles = Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !Program.IsInObjOrBin(f, projectDir))
            .ToArray();

        Console.WriteLine($"\n{projectName} ({sourceFiles.Length} files)");

        violations = sourceFiles.Sum(f => AnalyzeFile(f, analyzer!));

        if (violations == 0)
        {
            Program.PrintSuccess("  No violations.");
        }

        return true;
    }

    private static bool TryLoadAnalyzer(string configPath, string projectName, out IDependencyAnalyzer? analyzer)
    {
        analyzer = null;
        try
        {
            DependencyRuleSet ruleSet = DependencyGuardFactory.ParseFromYaml(File.ReadAllText(configPath));
            IReadOnlyList<RuleConflict> conflicts = DependencyGuardFactory.TryCreateAnalyzer([ruleSet], out analyzer);

            if (conflicts.Count > 0)
            {
                foreach (RuleConflict conflict in conflicts)
                {
                    Program.PrintError($"[error] {projectName}: {conflict.Message}");
                }

                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Program.PrintError($"[error] {projectName}: failed to parse config: {ex.Message}");
            return false;
        }
    }

    private static int AnalyzeFile(string sourceFile, IDependencyAnalyzer analyzer)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(File.ReadAllText(sourceFile), path: sourceFile);
        return AnalyzeUsingDirectives(sourceFile, tree, analyzer) + AnalyzeQualifiedNames(sourceFile, tree, analyzer);
    }

    private static int AnalyzeUsingDirectives(string sourceFile, SyntaxTree tree, IDependencyAnalyzer analyzer)
    {
        int violations = 0;

        foreach (UsingDirectiveSyntax usingDir in tree.GetCompilationUnitRoot().DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            if (usingDir.Alias is not null ||
                usingDir.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) ||
                usingDir.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword))
            {
                continue;
            }

            string targetNs = usingDir.NamespaceOrType.ToString();
            string? sourceNs = Program.GetContainingNamespace(usingDir);
            if (sourceNs is null)
            {
                continue;
            }

            DependencyResult result = analyzer.AnalyzeDependency(new(sourceNs, targetNs));
            if (!result.IsAllowed)
            {
                ReportViolation(sourceFile, usingDir.GetLocation(), result.Reason);
                violations++;
            }
        }

        return violations;
    }

    private static int AnalyzeQualifiedNames(string sourceFile, SyntaxTree tree, IDependencyAnalyzer analyzer)
    {
        int violations = 0;

        foreach (QualifiedNameSyntax qualName in tree.GetCompilationUnitRoot()
                     .DescendantNodes()
                     .OfType<QualifiedNameSyntax>())
        {
            if (qualName.Parent is QualifiedNameSyntax)
            {
                continue;
            }

            // The name of a namespace declaration (`namespace MyApp.Application`) is not a reference.
            if (qualName.Parent is BaseNamespaceDeclarationSyntax)
            {
                continue;
            }

            if (qualName.Ancestors().OfType<UsingDirectiveSyntax>().Any())
            {
                continue;
            }

            string targetNs = qualName.Left.ToString();
            string? sourceNs = Program.GetContainingNamespace(qualName);
            if (sourceNs is null)
            {
                continue;
            }

            if (targetNs == sourceNs)
            {
                continue;
            }

            DependencyResult result = analyzer.AnalyzeDependency(new(sourceNs, targetNs));
            if (!result.IsAllowed)
            {
                ReportViolation(sourceFile, qualName.GetLocation(), result.Reason);
                violations++;
            }
        }

        return violations;
    }

    private static void ReportViolation(string sourceFile, Location location, string? reason)
    {
        FileLinePositionSpan span = location.GetLineSpan();
        Program.PrintWarning($"  {sourceFile}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1}): warning DG0001: {reason}");
    }
}
