using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyGuard.Cli;

internal static partial class Program
{
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
        if (args.Length > 0 && args[0] == "generate")
        {
            return CommandGenerate.Run(args[1..]);
        }

        // "check" subcommand or bare invocation (backward compat)
        string[] checkArgs = args.Length > 0 && args[0] == "check" ? args[1..] : args;
        return CommandCheck.Run(checkArgs);
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

    internal static string? GetContainingNamespace(SyntaxNode node)
    {
        SyntaxNode? current = node.Parent;
        while (current is not null)
        {
            if (current is BaseNamespaceDeclarationSyntax ns)
            {
                return ns.Name.ToString();
            }

            current = current.Parent;
        }

        if (node.SyntaxTree.GetRoot() is CompilationUnitSyntax cu)
        {
            return cu.Members.OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString();
        }

        return null;
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
