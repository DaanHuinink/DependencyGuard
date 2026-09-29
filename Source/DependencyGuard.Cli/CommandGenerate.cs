using DependencyGuard.Core.Interfaces;
using DependencyGuard.Roslyn.Interfaces;
using Microsoft.CodeAnalysis;

namespace DependencyGuard.Cli;

internal static class CommandGenerate
{
    internal static async Task<int> RunAsync(string[] args)
    {
        string? outputPath = null;
        bool force = false;
        bool restore = true;
        Dictionary<string, Action<string>> valueOptions = new()
        {
            ["--output"] = value => outputPath = Path.GetFullPath(value),
            ["-o"] = value => outputPath = Path.GetFullPath(value),
        };
        Dictionary<string, Action> flags = new()
        {
            ["--force"] = () => force = true,
            ["--no-restore"] = () => restore = false,
        };

        string? usageError = Program.ParseArguments(args, valueOptions, flags, out string targetPath);
        if (usageError is not null)
        {
            return Program.UsageError(usageError);
        }

        using ProjectsLoader loader = new();
        (IReadOnlyList<Project> projects, string? error) = await loader.LoadAsync(targetPath, restore);
        if (error is not null)
        {
            return Program.UsageError(error);
        }

        bool written = outputPath is null
            ? await GeneratePerProjectAsync(projects, force)
            : await GenerateOneFileAsync(projects, outputPath, force);

        Console.WriteLine();
        if (!written)
        {
            Program.PrintError("Generation failed.");
            return 2;
        }

        Program.PrintSuccess("Generation complete.");
        return 0;
    }

    private static async Task<bool> GeneratePerProjectAsync(IReadOnlyList<Project> projects, bool force)
    {
        int errors = 0;
        foreach (IGrouping<string, Project> project in projects.GroupBy(p => p.FilePath!, StringComparer.OrdinalIgnoreCase))
        {
            string outPath = Path.Combine(Path.GetDirectoryName(project.Key)!, RoslynAnalyzer.ConfigFileName);
            if (!CanWrite(outPath, force, Path.GetFileNameWithoutExtension(project.Key)))
            {
                errors++;
                continue;
            }

            DependencyRuleSetBuilder builder = new();
            foreach (Project framework in project)
            {
                await CollectAsync(framework, builder);
            }

            Write(builder, outPath);
        }

        return errors == 0;
    }

    private static async Task<bool> GenerateOneFileAsync(IReadOnlyList<Project> projects, string outputPath, bool force)
    {
        if (!CanWrite(outputPath, force, "all projects"))
        {
            return false;
        }

        DependencyRuleSetBuilder builder = new();
        foreach (Project project in projects.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            await CollectAsync(project, builder);
        }

        Write(builder, outputPath);
        return true;
    }

    private static async Task CollectAsync(Project project, DependencyRuleSetBuilder builder)
    {
        Console.WriteLine($"\n{project.Name} ({project.DocumentIds.Count} files)");
        ProjectAnalysis analysis = await ProjectsAnalysis.AnalyzeAsync(project, ProjectsAnalysis.GetEmptyRuleFile(project));
        if (analysis.CompileErrors > 0)
        {
            Program.PrintWarning($"  [warn] {analysis.CompileErrors} compile error(s): names that do not resolve are left out.");
        }

        foreach (Diagnostic diagnostic in analysis.Diagnostics.Where(d => d.Id == "DG0001"))
        {
            if (diagnostic.Properties.TryGetValue(RoslynAnalyzer.SourceNamespaceProperty, out string? source) &&
                diagnostic.Properties.TryGetValue(RoslynAnalyzer.TargetNamespaceProperty, out string? target) &&
                source is not null && target is not null)
            {
                builder.AddAllow(source, target);
            }
        }
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
}
