#!/usr/bin/env dotnet

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

try
{
    string root = GetRepositoryRoot();
    string demo = GetDemoDirectory(root);
    string analyzerProject = Path.Combine(root, "Source", "DependencyGuard.Analyzer", "DependencyGuard.Analyzer.csproj");
    string localPackages = Path.Combine(root, "LocalPackages");

    // A new version for every run, so NuGet never serves a previously cached build.
    string version = CreateDevVersion(analyzerProject);
    Console.WriteLine($"Building DependencyGuard {version}");

    Run("dotnet", "build", Path.Combine(root, "DependencyGuard.slnx"), "-c", "Release");

    RemoveOldAnalyzerPackages(localPackages);
    Run("dotnet", "pack", analyzerProject, "-c", "Release", "-o", localPackages, $"-p:Version={version}");

    UpdateDemoPackageVersion(Path.Combine(demo, "Directory.Packages.props"), version);

    Run("dotnet", "build-server", "shutdown");
    Run("dotnet", "build", Path.Combine(demo, "DependencyGuard.Demo.slnx"));

    Console.WriteLine();
    Console.WriteLine($"The demo solution now uses DependencyGuard.Analyzer {version}.");
    return 0;
}
catch (Exception exception) when (exception is InvalidOperationException or IOException)
{
    Console.Error.WriteLine(exception.Message);
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

static string GetDemoDirectory(string root)
{
    return Directory
        .EnumerateDirectories(root)
        .FirstOrDefault(d => string.Equals(Path.GetFileName(d), "Demo", StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"No Demo directory found in {root}.");
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

static void RemoveOldAnalyzerPackages(string directory)
{
    if (!Directory.Exists(directory))
    {
        return;
    }

    foreach (string package in Directory.EnumerateFiles(directory, "DependencyGuard.Analyzer.*.nupkg"))
    {
        File.Delete(package);
    }
}

static void UpdateDemoPackageVersion(string propsPath, string version)
{
    Regex packageVersion = new("""(<PackageVersion\s+Include="DependencyGuard\.Analyzer"\s+Version=")[^"]*(")""");
    string content = File.ReadAllText(propsPath);
    if (!packageVersion.IsMatch(content))
    {
        throw new InvalidOperationException($"No PackageVersion for DependencyGuard.Analyzer found in {propsPath}.");
    }

    File.WriteAllText(propsPath, packageVersion.Replace(content, "${1}" + version + "${2}"));
    Console.WriteLine();
    Console.WriteLine($"Updated {propsPath} to {version}");
}

static void Run(string fileName, params string[] arguments)
{
    string commandLine = $"{fileName} {string.Join(' ', arguments)}";
    Console.WriteLine();
    Console.WriteLine($"> {commandLine}");

    ProcessStartInfo startInfo = new(fileName);
    foreach (string argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    using Process process = Process.Start(startInfo)
        ?? throw new InvalidOperationException($"Could not start '{fileName}'.");

    process.WaitForExit();

    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"'{commandLine}' failed with exit code {process.ExitCode}.");
    }
}
