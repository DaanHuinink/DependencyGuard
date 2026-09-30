using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace DependencyGuard.Cli;

internal sealed class ProjectsLoader : IDisposable
{
    private readonly MSBuildWorkspace _workspace = MSBuildWorkspace.Create();

    public ProjectsLoader()
    {
        _workspace.RegisterWorkspaceFailedHandler(e =>
        {
            if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
            {
                Program.PrintWarning($"[warn] {e.Diagnostic.Message}");
            }
        });
    }

    public async Task<(IReadOnlyList<Project> Projects, string? Error)> LoadAsync(string targetPath, bool restore)
    {
        if (!TryResolveTarget(targetPath, out string? solutionPath, out string[] projectPaths, out string? error))
        {
            return ([], error);
        }

        if (restore)
        {
            string[] restorePaths = solutionPath is null
                ? projectPaths
                : [solutionPath];
            foreach (string path in restorePaths)
            {
                string? restoreError = await RestoreAsync(path);
                if (restoreError is not null)
                {
                    return ([], restoreError);
                }
            }
        }

        if (solutionPath is not null)
        {
            Solution solution = await _workspace.OpenSolutionAsync(solutionPath);
            Project[] solutionProjects = solution.Projects
                .Where(p => p.Language == LanguageNames.CSharp)
                .ToArray();
            return (solutionProjects, null);
        }

        List<Project> projects = [];
        foreach (string projectPath in projectPaths)
        {
            if (!_workspace.CurrentSolution.Projects.Any(p => IsSamePath(p.FilePath, projectPath)))
            {
                await _workspace.OpenProjectAsync(projectPath);
            }

            projects.AddRange(_workspace.CurrentSolution.Projects
                .Where(p => IsSamePath(p.FilePath, projectPath) && p.Language == LanguageNames.CSharp));
        }

        return (projects, null);
    }

    public void Dispose()
    {
        _workspace.Dispose();
    }

    private static bool TryResolveTarget(string targetPath, out string? solutionPath, out string[] projectPaths, out string? error)
    {
        solutionPath = null;
        projectPaths = [];
        error = null;

        if (Directory.Exists(targetPath))
        {
            projectPaths = Directory.GetFiles(targetPath, "*.csproj", SearchOption.TopDirectoryOnly);
            if (projectPaths.Length == 0)
            {
                error = $"No .csproj found in: {targetPath}";
            }

            return projectPaths.Length > 0;
        }

        if (!File.Exists(targetPath))
        {
            error = $"File not found: {targetPath}";
            return false;
        }

        if (targetPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) || targetPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            solutionPath = targetPath;
            return true;
        }

        if (targetPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            projectPaths = [targetPath];
            return true;
        }

        error = $"Expected a .csproj, .sln, .slnx, or directory. Got: {targetPath}";
        return false;
    }

    private static async Task<string?> RestoreAsync(string path)
    {
        ProcessStartInfo start = new("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in new[] { "restore", path, "-v:q", "-nologo" })
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start 'dotnet restore'.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return process.ExitCode == 0
            ? null
            : $"'dotnet restore {path}' failed:{Environment.NewLine}{(await output + await errors).Trim()}";
    }

    private static bool IsSamePath(string? first, string second)
    {
        return first is not null && string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
    }
}
