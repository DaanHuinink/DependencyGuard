using System.Diagnostics;
using System.Text;
using NUnit.Framework;

namespace DependencyGuard.Tests.Integration;

[SetUpFixture]
public sealed class IntegrationSetupFixture
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(5);

    internal static string LocalPackagesDir { get; private set; } = null!;
    internal static string ToolProjectPath { get; private set; } = null!;
    internal static string AnalyzerPackageVersion { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task SetupAsync()
    {
        string solutionDir = FindSolutionDir();

        LocalPackagesDir = Path.Combine(Path.GetTempPath(), "DG_IntPkg_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(LocalPackagesDir);

        // Use a unique pre-release version so NuGet's global package cache never serves a stale build.
        AnalyzerPackageVersion = "0.1.0-int" + Guid.NewGuid().ToString("N")[..8];

        string analyzerProj = Path.Combine(solutionDir, "Source", "DependencyGuard.Analyzer", "DependencyGuard.Analyzer.csproj");
        ToolProjectPath = Path.Combine(solutionDir, "Source", "DependencyGuard.Cli", "DependencyGuard.Cli.csproj");

        (string packOutput, int packExitCode) = await RunAsync(
            "dotnet", $"pack \"{analyzerProj}\" -o \"{LocalPackagesDir}\" -c Release /p:Version={AnalyzerPackageVersion}");

        Assert.That(packExitCode, Is.EqualTo(0), $"Pack failed:\n{packOutput}");

        // Build the CLI once so the tests can use `dotnet run --no-build`.
        (string buildOutput, int buildExitCode) = await RunAsync("dotnet", $"build \"{ToolProjectPath}\"");

        Assert.That(buildExitCode, Is.EqualTo(0), $"CLI build failed:\n{buildOutput}");
    }

    [OneTimeTearDown]
    public void Teardown()
    {
        if (!Directory.Exists(LocalPackagesDir))
        {
            return;
        }

        try
        {
            Directory.Delete(LocalPackagesDir, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    private static string FindSolutionDir()
    {
        string? dir = Path.GetDirectoryName(typeof(IntegrationSetupFixture).Assembly.Location);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "DependencyGuard.slnx")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Could not locate DependencyGuard.slnx");
    }

    internal static async Task<(string Output, int ExitCode)> RunAsync(
        string executable, string arguments, string? workingDir = null)
    {
        ProcessStartInfo psi = new(executable, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDir ?? Directory.GetCurrentDirectory()
        };

        using Process process = Process.Start(psi)!;
        StringBuilder sb = new();

        // stdout and stderr are raised on different threads.
        DataReceivedEventHandler append = (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            lock (sb)
            {
                sb.AppendLine(e.Data);
            }
        };

        process.OutputDataReceived += append;
        process.ErrorDataReceived += append;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using CancellationTokenSource timeout = new(ProcessTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"'{executable} {arguments}' did not finish within {ProcessTimeout.TotalMinutes} minutes. Output so far:\n{sb}");
        }

        return (sb.ToString(), process.ExitCode);
    }
}
