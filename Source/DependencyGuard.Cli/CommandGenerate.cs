using DependencyGuard.Core.Interfaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyGuard.Cli;

internal static class CommandGenerate
{
    private const string DefaultOutputFileName = "dependency-guard.yaml";

    internal static int Run(string[] args)
    {
        ParseArgs(args, out string targetPath, out string? outputPath, out bool force);

        if (!Program.TryResolveProjectFiles(targetPath, out List<string> projectFiles, out string? error))
        {
            Program.PrintError(error);
            return 2;
        }

        int errors = 0;
        foreach (string projectFile in projectFiles)
        {
            if (!GenerateForProject(projectFile, outputPath, force))
            {
                errors++;
            }
        }

        Console.WriteLine();
        if (errors > 0)
        {
            Program.PrintError($"Failed to generate for {errors} project(s).");
            return 2;
        }

        Program.PrintSuccess("Generation complete.");
        return 0;
    }

    private static void ParseArgs(string[] args, out string targetPath, out string? outputPath, out bool force)
    {
        targetPath = Directory.GetCurrentDirectory();
        outputPath = null;
        force = false;

        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "--output" || args[i] == "-o") && i + 1 < args.Length)
            {
                outputPath = Path.GetFullPath(args[++i]);
            }
            else if (args[i] == "--force")
            {
                force = true;
            }
            else
            {
                targetPath = Path.GetFullPath(args[i]);
            }
        }
    }

    private static bool GenerateForProject(string projectFile, string? outputPath, bool force)
    {
        string projectDir = Path.GetDirectoryName(projectFile)!;
        string projectName = Path.GetFileNameWithoutExtension(projectFile);
        string outPath = outputPath ?? Path.Combine(projectDir, DefaultOutputFileName);

        if (File.Exists(outPath) && !force)
        {
            Program.PrintError($"[error] {projectName}: '{outPath}' already exists. Use --force to overwrite.");
            return false;
        }

        string[] sourceFiles = Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !Program.IsInObjOrBin(f, projectDir))
            .ToArray();

        Console.WriteLine($"\n{projectName} ({sourceFiles.Length} files)");

        DependencyRuleSetBuilder builder = new();

        foreach (string sourceFile in sourceFiles)
        {
            SyntaxTree tree = CSharpSyntaxTree.ParseText(File.ReadAllText(sourceFile), path: sourceFile);
            CollectUsingDependencies(tree, builder);
            CollectQualifiedNameDependencies(tree, builder);
        }

        DependencyRuleSet ruleSet = builder.Build();

        IReadOnlyList<RuleConflict> conflicts = DependencyGuardFactory.TryCreateAnalyzer(
            [ruleSet],
            out IDependencyAnalyzer? _);
        foreach (RuleConflict conflict in conflicts)
        {
            Program.PrintWarning($"  [warn] {conflict.Message}");
        }

        string yaml = DependencyGuardFactory.SerializeToYaml(ruleSet);
        File.WriteAllText(outPath, yaml);
        Program.PrintSuccess($"  Written: {outPath}");
        return true;
    }

    private static void CollectUsingDependencies(SyntaxTree tree, DependencyRuleSetBuilder builder)
    {
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

            if (targetNs == sourceNs)
            {
                continue;
            }

            builder.AddAllow(sourceNs, targetNs);
        }
    }

    private static void CollectQualifiedNameDependencies(SyntaxTree tree, DependencyRuleSetBuilder builder)
    {
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

            builder.AddAllow(sourceNs, targetNs);
        }
    }
}
