using Microsoft.CodeAnalysis;

namespace DependencyGuard.Analyzer;

internal static class AnalyzerDiagnostics
{
    public static readonly DiagnosticDescriptor WarningConfigurationMissing = new(
        id: "DG0000",
        title: "DependencyGuard configuration not found",
        messageFormat: $"No '{Analyzer.ConfigFileName}' additional file found. Add it to AdditionalFiles in your project file.",
        category: "Architecture",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    public static readonly DiagnosticDescriptor WarningDisallowedDependency = new(
        id: "DG0001",
        title: "Disallowed namespace dependency",
        messageFormat: "{0}",
        category: "Architecture",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "This dependency violates the configured architectural rules.");

    public static readonly DiagnosticDescriptor ErrorConflictingRules = new(
        id: "DG0003",
        title: "Conflicting dependency rules",
        messageFormat: "{0}",
        category: "Architecture",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    public static readonly DiagnosticDescriptor ErrorFailedToParseYaml = new(
        id: "DG0004",
        title: "Failed to parse YAML",
        messageFormat: "{0}",
        category: "Architecture",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    public static readonly DiagnosticDescriptor ErrorUnhandledException = new(
        id: "DG9999",
        title: "Unhandled exception",
        messageFormat: "{0}",
        category: "System",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);
}
