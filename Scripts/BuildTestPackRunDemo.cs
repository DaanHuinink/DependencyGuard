#!/usr/bin/env dotnet

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

try
{
    string root = GetRepositoryRoot();
    string solution = Path.Combine(root, "DependencyGuard.slnx");
    string demoSolution = Path.Combine(GetDemoDirectory(root), "DependencyGuard.Demo.slnx");
    string localPackages = Path.Combine(root, "LocalPackages");
    DemoProject[] demoProjects = GetDemoProjects(demoSolution);

    Run("dotnet", "build", solution, "-c", "Release");
    Run("dotnet", "test", solution, "-c", "Release", "--no-build");
    Run("dotnet", "run", Path.Combine(root, "Scripts", "UpdateDemoToLatestAnalyzer.cs"));

    // --no-incremental: projects that are already up to date don't print their warnings again.
    string demoOutput = RunAndCapture(0, "dotnet", "build", demoSolution, "--no-incremental", "-tl:off");
    CheckAnalyzerDiagnostics(demoProjects, demoOutput);

    string tool = InstallCliTool(root, localPackages);

    // The CLI exits with 1 when it finds violations, which the demos contain on purpose.
    string cliOutput = RunAndCapture(1, tool, demoSolution);
    CheckCliResults(demoProjects, cliOutput);

    GenerateDemoConfigs(tool, demoProjects, Path.Combine(localPackages, "Generated"));

    Console.WriteLine();
    Console.WriteLine("Sanity check passed.");
    return 0;
}
catch (Exception exception) when (exception is InvalidOperationException or IOException)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"Sanity check failed: {exception.Message}");
    return 1;
}

static string GetRepositoryRoot([CallerFilePath] string scriptPath = "")
{
    string? directory = Path.GetDirectoryName(scriptPath);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory, "DependencyGuard.slnx")))
        {
            return directory;
        }

        directory = Path.GetDirectoryName(directory);
    }

    throw new InvalidOperationException($"Could not find DependencyGuard.slnx above {scriptPath}.");
}

// Git tracks the folder as "demo" while Windows checkouts may show "Demo"; accept either.
static string GetDemoDirectory(string root)
{
    return Directory.EnumerateDirectories(root)
               .FirstOrDefault(d => string.Equals(Path.GetFileName(d), "Demo", StringComparison.OrdinalIgnoreCase))
           ?? throw new InvalidOperationException($"No Demo directory found in {root}.");
}

static DemoProject[] GetDemoProjects(string demoSolution)
{
    string demoDirectory = Path.GetDirectoryName(demoSolution)!;
    return XDocument.Load(demoSolution)
        .Descendants("Project")
        .Select(p => (string?)p.Attribute("Path"))
        .OfType<string>()
        .Select(path => new DemoProject(
            Path.GetFileNameWithoutExtension(path),
            Path.GetFullPath(Path.Combine(demoDirectory, path))))
        .ToArray();
}

static string CreateDevVersion(string projectPath)
{
    Match match = Regex.Match(File.ReadAllText(projectPath), "<Version>([^<]+)</Version>");
    if (!match.Success)
    {
        throw new InvalidOperationException($"No <Version> found in {projectPath}.");
    }

    string baseVersion = match.Groups[1].Value.Split('-')[0];
    return $"{baseVersion}-dev.{DateTime.UtcNow:yyyyMMddHHmmss}";
}

static void CheckAnalyzerDiagnostics(DemoProject[] projects, string buildOutput)
{
    // Matches e.g. "...Demo.cs(14,5): warning DG0001: ... [...\DependencyGuard.Demo.Notifications.csproj]"
    Regex diagnostic = new(@"(?<severity>warning|error) (?<id>DG\d{4}):.*\[(?<project>[^\]]+\.csproj)\]");

    List<(string Project, string Id, string Line)> found = [];
    foreach (string line in buildOutput.Split('\n').Select(l => l.Trim()).Distinct())
    {
        Match match = diagnostic.Match(line);
        if (match.Success)
        {
            string project = Path.GetFileNameWithoutExtension(match.Groups["project"].Value);
            found.Add((project, match.Groups["id"].Value, line));
        }
    }

    Console.WriteLine();
    Console.WriteLine("Analyzer diagnostics in the demo build:");
    foreach (DemoProject project in projects)
    {
        int violations = found.Count(d => d.Project == project.Name && d.Id == "DG0001");
        Console.WriteLine($"  {project.Name}: {violations} x DG0001");
    }

    string[] unexpected = found.Where(d => d.Id != "DG0001").Select(d => d.Line).ToArray();
    if (unexpected.Length > 0)
    {
        throw new InvalidOperationException(
            "the demo build reported unexpected diagnostics:" + Environment.NewLine + string.Join(Environment.NewLine, unexpected));
    }

    string[] withoutViolation = projects
        .Where(p => !found.Any(d => d.Project == p.Name && d.Id == "DG0001"))
        .Select(p => p.Name)
        .ToArray();
    if (withoutViolation.Length > 0)
    {
        throw new InvalidOperationException(
            $"these demo projects no longer report their intended DG0001 violation: {string.Join(", ", withoutViolation)}");
    }
}

static string InstallCliTool(string root, string localPackages)
{
    string cliProject = Path.Combine(root, "Source", "DependencyGuard.Cli", "DependencyGuard.Cli.csproj");
    string toolPath = Path.Combine(localPackages, "Tools");
    string version = CreateDevVersion(cliProject);

    foreach (string package in Directory.EnumerateFiles(localPackages, "DependencyGuard.Cli.*.nupkg"))
    {
        File.Delete(package);
    }

    if (Directory.Exists(toolPath))
    {
        Directory.Delete(toolPath, recursive: true);
    }

    Run("dotnet", "pack", cliProject, "-c", "Release", "-o", localPackages, $"-p:Version={version}");

    // --tool-path installs into a private folder instead of the global tools, so the machine's own dotnet tools are left alone.
    Run("dotnet", "tool", "install", "DependencyGuard.Cli", "--version", version, "--tool-path", toolPath, "--add-source", localPackages);

    return Path.Combine(toolPath, OperatingSystem.IsWindows() ? "dependency-guard.exe" : "dependency-guard");
}

static void CheckCliResults(DemoProject[] projects, string output)
{
    Regex header = new(@"^(?<project>\S+) \(\d+ files\)$");
    Regex skipped = new(@"^\[skip\] (?<project>[^:]+):");
    Regex colorCodes = new(@"\x1B\[[0-9;]*m");

    Dictionary<string, int> violations = [];
    HashSet<string> skippedProjects = [];
    List<string> errors = [];
    string? currentProject = null;

    foreach (string rawLine in output.Split('\n'))
    {
        string line = colorCodes.Replace(rawLine, string.Empty).Trim();
        Match headerMatch = header.Match(line);
        Match skippedMatch = skipped.Match(line);

        if (headerMatch.Success)
        {
            currentProject = headerMatch.Groups["project"].Value;
            violations[currentProject] = 0;
        }
        else if (skippedMatch.Success)
        {
            skippedProjects.Add(skippedMatch.Groups["project"].Value);
        }
        else if (line.StartsWith("[error]", StringComparison.Ordinal))
        {
            errors.Add(line);
        }
        else if (currentProject is not null && line.Contains(": warning DG0001:", StringComparison.Ordinal))
        {
            violations[currentProject]++;
        }
    }

    Console.WriteLine();
    Console.WriteLine("CLI results for the demo solution:");

    foreach (DemoProject project in projects)
    {
        string result = skippedProjects.Contains(project.Name)
            ? "skipped (no dependency-guard.yaml of its own; the CLI does not read MSBuild settings)"
            : $"{violations.GetValueOrDefault(project.Name)} x DG0001";
        Console.WriteLine($"  {project.Name}: {result}");
    }

    if (errors.Count > 0)
    {
        throw new InvalidOperationException(
            "the CLI reported errors:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
    }

    string[] withoutViolation = projects
        .Where(p => !skippedProjects.Contains(p.Name) && violations.GetValueOrDefault(p.Name) == 0)
        .Select(p => p.Name)
        .ToArray();

    if (withoutViolation.Length > 0)
    {
        throw new InvalidOperationException(
            $"the CLI found no DG0001 violation in: {string.Join(", ", withoutViolation)}");
    }
}

static void GenerateDemoConfigs(string tool, DemoProject[] projects, string outputDirectory)
{
    if (Directory.Exists(outputDirectory))
    {
        Directory.Delete(outputDirectory, recursive: true);
    }

    foreach (DemoProject project in projects)
    {
        string config = Path.Combine(outputDirectory, project.Name, "dependency-guard.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);

        Run(tool, "generate", "--output", config, project.Path);

        // A generated config allows every dependency the project has today, so checking the
        // project against it must not find any violations.
        Run(tool, "--config", config, project.Path);
    }

    Console.WriteLine();
    Console.WriteLine($"Generated configs are in {outputDirectory}");
}

static void Run(string fileName, params string[] arguments)
{
    using Process process = Start(fileName, arguments, redirectOutput: false);
    process.WaitForExit();
    ThrowIfUnexpectedExitCode(process, 0, fileName, arguments);
}

static string RunAndCapture(int expectedExitCode, string fileName, params string[] arguments)
{
    StringBuilder output = new();
    using Process process = Start(fileName, arguments, redirectOutput: true);

    // stdout and stderr are raised on different threads; show the output live and keep a copy.
    DataReceivedEventHandler append = (_, e) =>
    {
        if (e.Data is null)
        {
            return;
        }

        lock (output)
        {
            output.AppendLine(e.Data);
            Console.WriteLine(e.Data);
        }
    };

    process.OutputDataReceived += append;
    process.ErrorDataReceived += append;
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    process.WaitForExit();

    ThrowIfUnexpectedExitCode(process, expectedExitCode, fileName, arguments);
    return output.ToString();
}

static Process Start(string fileName, string[] arguments, bool redirectOutput)
{
    Console.WriteLine();
    Console.WriteLine($"> {fileName} {string.Join(' ', arguments)}");

    ProcessStartInfo startInfo = new(fileName) { RedirectStandardOutput = redirectOutput, RedirectStandardError = redirectOutput };

    foreach (string argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    return Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start '{fileName}'.");
}

static void ThrowIfUnexpectedExitCode(Process process, int expectedExitCode, string fileName, string[] arguments)
{
    if (process.ExitCode != expectedExitCode)
    {
        throw new InvalidOperationException(
            $"'{fileName} {string.Join(' ', arguments)}' exited with code {process.ExitCode}, expected {expectedExitCode}.");
    }
}

internal sealed record DemoProject(string Name, string Path);
