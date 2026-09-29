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
        List<string> globalConfigPaths = [];
        Dictionary<string, Action<string>> valueOptions = new()
        {
            ["--config"] = value => globalConfigPaths.Add(Path.GetFullPath(value)),
            ["-c"] = value => globalConfigPaths.Add(Path.GetFullPath(value)),
        };

        string? usageError = Program.ParseArguments(args, valueOptions, new Dictionary<string, Action>(), out string targetPath);
        if (usageError is not null)
        {
            return Program.UsageError(usageError);
        }

        string? missingConfig = globalConfigPaths.FirstOrDefault(path => !File.Exists(path));
        if (missingConfig is not null)
        {
            return Program.UsageError($"Config file not found: {missingConfig}");
        }

        if (!Program.TryResolveProjectFiles(targetPath, out List<string> projectFiles, out string? error))
        {
            return Program.UsageError(error!);
        }

        int totalViolations = 0;
        int invalidConfigs = 0;
        foreach (string projectFile in projectFiles)
        {
            if (TryAnalyzeProject(projectFile, globalConfigPaths, out int violations))
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

    // Returns false when the project's configuration can't be parsed or contains conflicting rules.
    private static bool TryAnalyzeProject(string projectFile, IReadOnlyList<string> globalConfigPaths, out int violations)
    {
        violations = 0;
        string projectDir = Path.GetDirectoryName(projectFile)!;
        string projectName = Path.GetFileNameWithoutExtension(projectFile);

        // Like the analyzer: the --config files and the project's own file, merged.
        string projectConfigPath = Path.Combine(projectDir, DefaultConfigFileName);
        List<string> configPaths = [.. globalConfigPaths];
        if (File.Exists(projectConfigPath) && !configPaths.Contains(projectConfigPath, StringComparer.OrdinalIgnoreCase))
        {
            configPaths.Add(projectConfigPath);
        }

        if (configPaths.Count == 0)
        {
            Console.WriteLine($"[skip] {projectName}: no {DefaultConfigFileName} found");
            return true;
        }

        if (!TryLoadAnalyzer(configPaths, projectName, out IDependencyAnalyzer? analyzer))
        {
            return false;
        }

        string[] sourceFiles = Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !Program.IsInObjOrBin(f, projectDir))
            .ToArray();

        Console.WriteLine($"\n{projectName} ({sourceFiles.Length} files)");

        string rootNamespace = Program.GetRootNamespace(projectFile);
        violations = sourceFiles.Sum(f => AnalyzeFile(f, rootNamespace, analyzer!));

        if (violations == 0)
        {
            Program.PrintSuccess("  No violations.");
        }

        return true;
    }

    private static bool TryLoadAnalyzer(IReadOnlyList<string> configPaths, string projectName, out IDependencyAnalyzer? analyzer)
    {
        analyzer = null;
        List<DependencyRuleSet> ruleSets = [];
        bool isValid = true;
        foreach (string configPath in configPaths)
        {
            try
            {
                ruleSets.Add(DependencyGuardFactory.ParseFromYaml(File.ReadAllText(configPath), configPath));
            }
            catch (RuleSetException exception)
            {
                foreach (RuleSetError error in exception.Errors)
                {
                    string place = error.Location is null
                        ? configPath
                        : $"{error.Location.FilePath}({error.Location.Line + 1},{error.Location.Column + 1})";
                    Program.PrintError($"[error] {projectName}: {place}: error DG0004: {error.Message}");
                }

                isValid = false;
            }
            catch (Exception ex)
            {
                Program.PrintError($"[error] {projectName}: failed to parse config {configPath}: {ex.Message}");
                isValid = false;
            }
        }

        if (!isValid)
        {
            return false;
        }

        IReadOnlyList<RuleConflict> conflicts = DependencyGuardFactory.TryCreateAnalyzer(ruleSets, out analyzer);
        foreach (RuleConflict conflict in conflicts)
        {
            Program.PrintError($"[error] {projectName}: {conflict.Message}");
        }

        return conflicts.Count == 0;
    }

    private static int AnalyzeFile(string sourceFile, string rootNamespace, IDependencyAnalyzer analyzer)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(File.ReadAllText(sourceFile), path: sourceFile);
        return AnalyzeUsingDirectives(sourceFile, tree, rootNamespace, analyzer)
               + AnalyzeQualifiedNames(sourceFile, tree, rootNamespace, analyzer);
    }

    private static int AnalyzeUsingDirectives(string sourceFile, SyntaxTree tree, string rootNamespace, IDependencyAnalyzer analyzer)
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
            foreach (string sourceNs in Program.GetUsingSourceNamespaces(usingDir, rootNamespace))
            {
                if (targetNs == sourceNs)
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
        }

        return violations;
    }

    private static int AnalyzeQualifiedNames(string sourceFile, SyntaxTree tree, string rootNamespace, IDependencyAnalyzer analyzer)
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
            string sourceNs = Program.GetContainingNamespace(qualName, rootNamespace);
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
