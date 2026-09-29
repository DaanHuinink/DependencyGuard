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
        string? outputPath = null;
        bool force = false;
        Dictionary<string, Action<string>> valueOptions = new()
        {
            ["--output"] = value => outputPath = Path.GetFullPath(value),
            ["-o"] = value => outputPath = Path.GetFullPath(value),
        };
        Dictionary<string, Action> flags = new() { ["--force"] = () => force = true };

        string? usageError = Program.ParseArguments(args, valueOptions, flags, out string targetPath);
        if (usageError is not null)
        {
            return Program.UsageError(usageError);
        }

        if (!Program.TryResolveProjectFiles(targetPath, out List<string> projectFiles, out string? error))
        {
            return Program.UsageError(error!);
        }

        bool written = outputPath is null
            ? GeneratePerProject(projectFiles, force)
            : GenerateOneFile(projectFiles, outputPath, force);

        Console.WriteLine();
        if (!written)
        {
            Program.PrintError("Generation failed.");
            return 2;
        }

        Program.PrintSuccess("Generation complete.");
        return 0;
    }

    private static bool GeneratePerProject(IReadOnlyList<string> projectFiles, bool force)
    {
        int errors = 0;
        foreach (string projectFile in projectFiles)
        {
            string outPath = Path.Combine(Path.GetDirectoryName(projectFile)!, DefaultOutputFileName);
            if (!CanWrite(outPath, force, Path.GetFileNameWithoutExtension(projectFile)))
            {
                errors++;
                continue;
            }

            DependencyRuleSetBuilder builder = new();
            CollectProject(projectFile, builder);
            Write(builder, outPath);
        }

        return errors == 0;
    }

    // With --output, the rules of every project go into one file, e.g. one for the whole solution.
    private static bool GenerateOneFile(IReadOnlyList<string> projectFiles, string outputPath, bool force)
    {
        if (!CanWrite(outputPath, force, "all projects"))
        {
            return false;
        }

        DependencyRuleSetBuilder builder = new();
        foreach (string projectFile in projectFiles)
        {
            CollectProject(projectFile, builder);
        }

        Write(builder, outputPath);
        return true;
    }

    private static bool CanWrite(string outPath, bool force, string subject)
    {
        if (File.Exists(outPath) && !force)
        {
            Program.PrintError($"[error] {subject}: '{outPath}' already exists. Use --force to overwrite.");
            return false;
        }

        return true;
    }

    private static void CollectProject(string projectFile, DependencyRuleSetBuilder builder)
    {
        string projectDir = Path.GetDirectoryName(projectFile)!;
        string rootNamespace = Program.GetRootNamespace(projectFile);
        string[] sourceFiles = Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !Program.IsInObjOrBin(f, projectDir))
            .ToArray();

        Console.WriteLine($"\n{Path.GetFileNameWithoutExtension(projectFile)} ({sourceFiles.Length} files)");

        foreach (string sourceFile in sourceFiles)
        {
            SyntaxTree tree = CSharpSyntaxTree.ParseText(File.ReadAllText(sourceFile), path: sourceFile);
            CollectUsingDependencies(tree, rootNamespace, builder);
            CollectQualifiedNameDependencies(tree, rootNamespace, builder);
        }
    }

    private static void Write(DependencyRuleSetBuilder builder, string outPath)
    {
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
    }

    private static void CollectUsingDependencies(SyntaxTree tree, string rootNamespace, DependencyRuleSetBuilder builder)
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
            foreach (string sourceNs in Program.GetUsingSourceNamespaces(usingDir, rootNamespace))
            {
                if (targetNs != sourceNs)
                {
                    builder.AddAllow(sourceNs, targetNs);
                }
            }
        }
    }

    private static void CollectQualifiedNameDependencies(SyntaxTree tree, string rootNamespace, DependencyRuleSetBuilder builder)
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
            string sourceNs = Program.GetContainingNamespace(qualName, rootNamespace);
            if (targetNs == sourceNs)
            {
                continue;
            }

            builder.AddAllow(sourceNs, targetNs);
        }
    }
}
