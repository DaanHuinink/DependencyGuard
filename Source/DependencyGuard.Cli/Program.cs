using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyGuard.Cli;

internal static partial class Program
{
    private const string Usage = """
        Usage:
          dependency-guard [check] [--config <file>]... [<target>]
          dependency-guard generate [--output <file>] [--force] [<target>]

        <target>  A .csproj, .sln or .slnx file, or a folder with a .csproj. Default: the current folder.

        check     Checks every project against its own dependency-guard.yaml and the --config files, merged.
                  Exit codes: 0 no violations, 1 violations or an invalid rule file, 2 usage error.
        generate  Writes a dependency-guard.yaml that allows every dependency the code has today: one per project,
                  or one for all projects with --output. Exit codes: 0 written, 2 failed or usage error.

        Options:
          -c, --config <file>  A rule file for every project, on top of the project's own. May be repeated.
          -o, --output <file>  Write one rule file for all projects.
              --force          Overwrite an existing rule file.
          -h, --help           Show this help.
              --version        Show the version.

        The CLI reads the .cs files in a project's folder, without building: it checks using directives and
        fully qualified names. The analyzer checks more (every name, Razor files); see the README.
        """;

    private static int Main(string[] args)
    {
        try
        {
            return RunMain(args);
        }
        catch (Exception ex)
        {
            PrintError($"Unexpected error: {ex.Message}");
            return 2;
        }
    }

    private static int RunMain(string[] args)
    {
        if (args.Any(a => a is "--help" or "-h" or "-?" or "/?"))
        {
            Console.WriteLine($"DependencyGuard {GetVersion()}: namespace dependency rules for C#.");
            Console.WriteLine();
            Console.WriteLine(Usage);
            return 0;
        }

        if (args.Any(a => a == "--version"))
        {
            Console.WriteLine(GetVersion());
            return 0;
        }

        if (args.Length > 0 && args[0] == "generate")
        {
            return CommandGenerate.Run(args[1..]);
        }

        // "check" subcommand or bare invocation (backward compat)
        string[] checkArgs = args.Length > 0 && args[0] == "check" ? args[1..] : args;
        return CommandCheck.Run(checkArgs);
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

    internal static bool IsInObjOrBin(string filePath, string projectDir)
    {
        string rel = Path.GetRelativePath(projectDir, filePath);
        return rel.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || rel.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    // The full name of the namespace a node is in (A.B for `namespace A { namespace B { } }`), or the project's root
    // namespace for code outside every namespace.
    internal static string GetContainingNamespace(SyntaxNode node, string rootNamespace)
    {
        string[] names = node.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Reverse()
            .Select(ns => ns.Name.ToString())
            .ToArray();

        return names.Length > 0 ? string.Join(".", names) : rootNamespace;
    }

    // A using directive belongs to the namespace it is written in; one above all namespaces serves the whole file:
    // every namespace in it, and the root namespace for code outside them.
    internal static IEnumerable<string> GetUsingSourceNamespaces(UsingDirectiveSyntax usingDirective, string rootNamespace)
    {
        if (usingDirective.Parent is BaseNamespaceDeclarationSyntax declaration)
        {
            return [GetContainingNamespace(declaration.Name, rootNamespace)];
        }

        CompilationUnitSyntax file = (CompilationUnitSyntax)usingDirective.Parent!;
        List<string> namespaces = file.Members
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Select(ns => ns.Name.ToString())
            .ToList();

        if (namespaces.Count == 0 || file.Members.Any(member => member is not BaseNamespaceDeclarationSyntax))
        {
            namespaces.Add(rootNamespace);
        }

        return namespaces.Distinct(StringComparer.Ordinal);
    }

    // What MSBuild would use as RootNamespace: the project's <RootNamespace>, else the project file's name.
    internal static string GetRootNamespace(string projectFile)
    {
        try
        {
            string? rootNamespace = XDocument.Load(projectFile)
                .Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "RootNamespace")
                ?.Value
                .Trim();

            if (!string.IsNullOrEmpty(rootNamespace) && !rootNamespace!.Contains("$("))
            {
                return rootNamespace;
            }
        }
        catch (System.Xml.XmlException)
        {
            // An unreadable project file: fall back to its name.
        }

        return Path.GetFileNameWithoutExtension(projectFile);
    }

    internal static bool TryResolveProjectFiles(string targetPath, out List<string> projectFiles, out string? error)
    {
        projectFiles = [];
        error = null;

        if (Directory.Exists(targetPath))
        {
            projectFiles = [.. Directory.GetFiles(targetPath, "*.csproj", SearchOption.TopDirectoryOnly)];
            if (projectFiles.Count == 0)
            {
                error = $"No .csproj found in: {targetPath}";
            }

            return projectFiles.Count > 0;
        }

        if (!File.Exists(targetPath))
        {
            error = $"File not found: {targetPath}";
            return false;
        }

        if (targetPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            projectFiles = ParseSlnx(targetPath);
        }
        else if (targetPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            projectFiles = ParseSln(targetPath);
        }
        else if (targetPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            projectFiles = [targetPath];
        }
        else
        {
            error = $"Expected a .csproj, .sln, .slnx, or directory. Got: {targetPath}";
            return false;
        }

        return true;
    }

    // Reads `--name value` options and at most one target; returns an error for anything else.
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

    private static List<string> ParseSlnx(string path)
    {
        string dir = Path.GetDirectoryName(path)!;
        return XDocument.Load(path)
            .Descendants("Project")
            .Select(e => e.Attribute("Path")?.Value)
            .Where(p => p is not null && p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Select(p => Path.GetFullPath(Path.Combine(dir, p!.Replace('/', Path.DirectorySeparatorChar))))
            .ToList();
    }

    private static List<string> ParseSln(string path)
    {
        string dir = Path.GetDirectoryName(path)!;
        return File.ReadLines(path)
            .Select(line => CreateProjectMatcherRegex().Match(line))
            .Where(m => m.Success)
            .Select(m => Path.GetFullPath(Path.Combine(dir, m.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar))))
            .ToList();
    }

    [GeneratedRegex(@"Project\(.*?\) = ""[^""]+"", ""([^""]+\.csproj)""")]
    private static partial Regex CreateProjectMatcherRegex();
}
