using Microsoft.CodeAnalysis;

namespace Spectral.Net.Analyzer;

/// <summary>
/// Contains all Roslyn <see cref="DiagnosticDescriptor"/> definitions used by the Spectral analyzer.
/// </summary>
internal static class SpectralDiagnosticDescriptors
{
    private const string Category = "Spectral";

    private const string HelpLinkUri =
        "https://github.com/sander1095/Spectral.VisualStudio#diagnostics";

    /// <summary>SPECTRAL000 – internal analyzer failure (e.g. Spectral CLI not found).</summary>
    public static readonly DiagnosticDescriptor AnalyzerError = new(
        id: "SPECTRAL000",
        title: "Spectral Analyzer Error",
        messageFormat: "Spectral analyzer encountered an error: {0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The Spectral analyzer failed to run. Verify that the Spectral CLI is installed " +
                     "(npm install -g @stoplight/spectral-cli) and the SpectralPath MSBuild property is correct.",
        helpLinkUri: HelpLinkUri,
        customTags: ["CompilationEnd"]);

    /// <summary>SPECTRAL001 – Spectral severity 0 (error).</summary>
    public static readonly DiagnosticDescriptor Error = new(
        id: "SPECTRAL001",
        title: "Spectral Error",
        messageFormat: "[{0}] {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A Spectral API linting error was found.",
        helpLinkUri: HelpLinkUri);

    /// <summary>SPECTRAL002 – Spectral severity 1 (warning).</summary>
    public static readonly DiagnosticDescriptor Warning = new(
        id: "SPECTRAL002",
        title: "Spectral Warning",
        messageFormat: "[{0}] {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A Spectral API linting warning was found.",
        helpLinkUri: HelpLinkUri);

    /// <summary>SPECTRAL003 – Spectral severity 2 (info / suggestion).</summary>
    public static readonly DiagnosticDescriptor Information = new(
        id: "SPECTRAL003",
        title: "Spectral Information",
        messageFormat: "[{0}] {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A Spectral API linting informational message was found.",
        helpLinkUri: HelpLinkUri);

    /// <summary>SPECTRAL004 – Spectral severity 3 (hint).</summary>
    public static readonly DiagnosticDescriptor Hint = new(
        id: "SPECTRAL004",
        title: "Spectral Hint",
        messageFormat: "[{0}] {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A Spectral API linting hint was found.",
        helpLinkUri: HelpLinkUri);
}
