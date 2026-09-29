using System.Reflection;
using Microsoft.CodeAnalysis;

namespace DependencyGuard.Cli;

internal static class Program
{
    private const string Usage = """
        Usage:
          dependency-guard [check] [--config <file>]... [--no-restore] [<target>]
          dependency-guard generate [--output <file>] [--force] [--no-restore] [<target>]

        <target>  A .csproj, .sln or .slnx file, or a folder with a .csproj. Default: the current folder.

        check     Checks every project the way the build does (the same analyzer, on the same compilation) against its
                  rule files, merged with the --config files.
                  Exit codes: 0 no violations, 1 violations or an invalid rule file, 2 usage error.
        generate  Writes a dependency-guard.yaml that allows every dependency the code has today: one per project,
                  or one for all projects with --output. Exit codes: 0 written, 2 failed or usage error.

        Options:
          -c, --config <file>  A rule file for every project, on top of the project's own. May be repeated.
          -o, --output <file>  Write one rule file for all projects.
              --force          Overwrite an existing rule file.
              --no-restore     Skip 'dotnet restore' (the projects must be restored already).
          -h, --help           Show this help.
              --version        Show the version.

        Needs the .NET 10 SDK: MSBuild evaluates the projects and they are compiled in memory, source generators
        (Razor) included, without a build.
        """;

    private static async Task<int> Main(string[] args)
    {
        try
        {
            return await RunMainAsync(args);
        }
        catch (Exception ex)
        {
            PrintError($"Unexpected error: {ex.Message}");
            return 2;
        }
    }

    private static Task<int> RunMainAsync(string[] args)
    {
        if (args.Any(a => a is "--help" or "-h" or "-?" or "/?"))
        {
            Console.WriteLine($"DependencyGuard {GetVersion()}: namespace dependency rules for C#.");
            Console.WriteLine();
            Console.WriteLine(Usage);
            return Task.FromResult(0);
        }

        if (args.Any(a => a == "--version"))
        {
            Console.WriteLine(GetVersion());
            return Task.FromResult(0);
        }

        if (args.Length > 0 && args[0] == "generate")
        {
            return CommandGenerate.RunAsync(args[1..]);
        }

        // "check" subcommand or bare invocation (backward compat)
        string[] checkArgs = args.Length > 0 && args[0] == "check" ? args[1..] : args;
        return CommandCheck.RunAsync(checkArgs);
    }

    internal static int UsageError(string message)
    {
        PrintError(message);
        Console.Error.WriteLine("Run 'dependency-guard --help' for the options.");
        return 2;
    }

    private static string GetVersion()
    {
        string version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                         ?? typeof(Program).Assembly.GetName().Version?.ToString()
                         ?? "unknown";

        // The SDK appends the commit (1.2.3+abcdef): the version alone is enough here.
        return version.Split('+')[0];
    }

    // Like the build: path(line,column): severity id: message, with the path of the .razor file for Razor code.
    internal static string Format(Diagnostic diagnostic)
    {
        string severity = diagnostic.Severity == DiagnosticSeverity.Error ? "error" : "warning";
        string text = $"{severity} {diagnostic.Id}: {diagnostic.GetMessage()}";
        if (diagnostic.Location == Location.None)
        {
            return text;
        }

        FileLinePositionSpan span = diagnostic.Location.GetMappedLineSpan();
        return $"{span.Path}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1}): {text}";
    }

    internal static void PrintSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    internal static void PrintWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    internal static void PrintError(string? message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine(message);
        Console.ResetColor();
    }

    // Reads `--name value` options, flags and at most one target; returns an error for anything else.
    internal static string? ParseArguments(
        string[] args,
        IReadOnlyDictionary<string, Action<string>> valueOptions,
        IReadOnlyDictionary<string, Action> flags,
        out string targetPath)
    {
        targetPath = Directory.GetCurrentDirectory();
        bool hasTarget = false;
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (valueOptions.TryGetValue(arg, out Action<string>? setValue))
            {
                if (i + 1 >= args.Length)
                {
                    return $"{arg} needs a value.";
                }

                setValue(args[++i]);
            }
            else if (flags.TryGetValue(arg, out Action? setFlag))
            {
                setFlag();
            }
            else if (arg.StartsWith("-", StringComparison.Ordinal) && arg.Length > 1)
            {
                return $"Unknown option: {arg}";
            }
            else if (hasTarget)
            {
                return $"More than one target: {targetPath} and {arg}";
            }
            else
            {
                targetPath = Path.GetFullPath(arg);
                hasTarget = true;
            }
        }

        return null;
    }
}
