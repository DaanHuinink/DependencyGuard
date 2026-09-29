using DependencyGuard.Roslyn.Interfaces;
using Microsoft.CodeAnalysis;

namespace DependencyGuard.Cli;

internal static class CommandCheck
{
    internal static async Task<int> RunAsync(string[] args)
    {
        List<string> configPaths = [];
        bool restore = true;
        Dictionary<string, Action<string>> valueOptions = new()
        {
            ["--config"] = value => configPaths.Add(Path.GetFullPath(value)),
            ["-c"] = value => configPaths.Add(Path.GetFullPath(value)),
        };
        Dictionary<string, Action> flags = new() { ["--no-restore"] = () => restore = false };

        string? usageError = Program.ParseArguments(args, valueOptions, flags, out string targetPath);
        if (usageError is not null)
        {
            return Program.UsageError(usageError);
        }

        string? missingConfig = configPaths.FirstOrDefault(path => !File.Exists(path));
        if (missingConfig is not null)
        {
            return Program.UsageError($"Config file not found: {missingConfig}");
        }

        using ProjectsLoader loader = new();
        (IReadOnlyList<Project> projects, string? error) = await loader.LoadAsync(targetPath, restore);
        if (error is not null)
        {
            return Program.UsageError(error);
        }

        int totalViolations = 0;
        int invalidConfigs = 0;
        foreach (Project project in projects.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            (int violations, bool isValid) = await CheckProjectAsync(project, configPaths);
            totalViolations += violations;
            if (!isValid)
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

    private static async Task<(int Violations, bool IsValid)> CheckProjectAsync(Project project, IReadOnlyList<string> configPaths)
    {
        IReadOnlyList<AdditionalText> ruleFiles = ProjectsAnalysis.GetRuleFiles(project, configPaths);
        if (ruleFiles.Count == 0)
        {
            Console.WriteLine($"[skip] {project.Name}: no {RoslynAnalyzerContract.ConfigFileName} found");
            return (0, true);
        }

        Console.WriteLine($"\n{project.Name} ({project.DocumentIds.Count} files)");
        ProjectAnalysis analysis = await ProjectsAnalysis.AnalyzeAsync(project, ruleFiles);
        if (analysis.CompileErrors > 0)
        {
            Program.PrintWarning($"  [warn] {analysis.CompileErrors} compile error(s): names that do not resolve are not checked.");
        }

        int violations = 0;
        bool isValid = true;
        IEnumerable<Diagnostic> inFileOrder = analysis.Diagnostics
            .OrderBy(d => d.Location.GetMappedLineSpan().Path, StringComparer.Ordinal)
            .ThenBy(d => d.Location.GetMappedLineSpan().StartLinePosition);
        foreach (Diagnostic diagnostic in inFileOrder)
        {
            if (diagnostic.Id == "DG0001")
            {
                Program.PrintWarning($"  {Program.Format(diagnostic)}");
                violations++;
            }
            else
            {
                Program.PrintError($"[error] {project.Name}: {Program.Format(diagnostic)}");
                isValid = false;
            }
        }

        if (violations == 0 && isValid)
        {
            Program.PrintSuccess("  No violations.");
        }

        return (violations, isValid);
    }
}
