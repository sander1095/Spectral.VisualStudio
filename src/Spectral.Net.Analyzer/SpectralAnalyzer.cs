using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Spectral.Net.Analyzer.Services;

namespace Spectral.Net.Analyzer;

/// <summary>
/// Roslyn <see cref="DiagnosticAnalyzer"/> that runs the Spectral CLI against every
/// YAML/JSON file included as an <c>&lt;AdditionalFiles&gt;</c> item in the project and
/// reports the results as build diagnostics (SPECTRAL001–SPECTRAL004).
///
/// <para>
/// Users configure behaviour through MSBuild properties (all optional):
/// <list type="bullet">
///   <item><term>SpectralPath</term><description>Path/name of the Spectral executable. Default: <c>spectral</c>.</description></item>
///   <item><term>SpectralRulesetFile</term><description>Path or URL of the Spectral ruleset. Auto-detected when empty.</description></item>
///   <item><term>SpectralEnabled</term><description>Set to <c>false</c> to disable analysis. Default: <c>true</c>.</description></item>
/// </list>
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp, LanguageNames.VisualBasic)]
public sealed class SpectralAnalyzer : DiagnosticAnalyzer
{
    // Extensions of files that will be linted when added as AdditionalFiles
    private static readonly HashSet<string> LintableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".yaml", ".yml", ".json",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            SpectralDiagnosticDescriptors.AnalyzerError,
            SpectralDiagnosticDescriptors.Error,
            SpectralDiagnosticDescriptors.Warning,
            SpectralDiagnosticDescriptors.Information,
            SpectralDiagnosticDescriptors.Hint);

    public override void Initialize(AnalysisContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));

        // Generated code never contains OpenAPI specs, so skip it
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        // Spectral runs as an external process – concurrent execution is safe
        context.EnableConcurrentExecution();

        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    // -------------------------------------------------------------------------
    // Core analysis
    // -------------------------------------------------------------------------

    private static void AnalyzeCompilation(CompilationAnalysisContext ctx)
    {
        var globalOptions = ctx.Options.AnalyzerConfigOptionsProvider.GlobalOptions;

        // --- Read MSBuild / analyzer config ----------------------------------
        var enabled = GetBoolProperty(globalOptions, "build_property.SpectralEnabled", defaultValue: true);
        if (!enabled)
        {
            return;
        }

        var spectralPath = GetStringProperty(globalOptions, "build_property.SpectralPath", defaultValue: "spectral");
        var rulesetFile = GetStringProperty(globalOptions, "build_property.SpectralRulesetFile", defaultValue: "");
        var projectDir = GetStringProperty(globalOptions, "build_property.MSBuildProjectDirectory", defaultValue: "");

        // --- Determine working directory ------------------------------------
        // Fall back to a harmless empty string; the linter service handles it gracefully.
        var workingDirectory = projectDir;

        // --- Lint each additional YAML/JSON file ----------------------------
        var linterService = new SpectralLinterService();

        foreach (var additionalFile in ctx.Options.AdditionalFiles)
        {
            if (!IsLintableFile(additionalFile.Path))
            {
                continue;
            }

            var result = linterService.LintFile(
                additionalFile.Path,
                spectralPath,
                rulesetFile,
                workingDirectory,
                ctx.CancellationToken);

            if (!result.IsSuccess)
            {
                // Report the tool error once, without a file location
                ctx.ReportDiagnostic(Diagnostic.Create(
                    SpectralDiagnosticDescriptors.AnalyzerError,
                    Location.None,
                    result.ErrorMessage));
                continue;
            }

            // Read the file text once so we can create precise Locations
            var fileText = additionalFile.GetText(ctx.CancellationToken);

            foreach (var diag in result.Diagnostics)
            {
                var descriptor = GetDescriptor(diag.Severity);
                var location = CreateLocation(additionalFile.Path, diag, fileText);
                ctx.ReportDiagnostic(Diagnostic.Create(descriptor, location, diag.Code, diag.Message));
            }
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static bool IsLintableFile(string filePath) =>
        LintableExtensions.Contains(Path.GetExtension(filePath));

    private static DiagnosticDescriptor GetDescriptor(int spectralSeverity) =>
        spectralSeverity switch
        {
            0 => SpectralDiagnosticDescriptors.Error,
            1 => SpectralDiagnosticDescriptors.Warning,
            2 => SpectralDiagnosticDescriptors.Information,
            _ => SpectralDiagnosticDescriptors.Hint,
        };

    private static Location CreateLocation(string filePath, SpectralDiagnostic diag, SourceText? text)
    {
        var startLine = Math.Max(0, diag.StartLine);
        var startCol = Math.Max(0, diag.StartColumn);
        var endLine = Math.Max(0, diag.EndLine);
        var endCol = Math.Max(0, diag.EndColumn);

        // Ensure end position is not before start position
        if (endLine < startLine || (endLine == startLine && endCol < startCol))
        {
            endLine = startLine;
            endCol = startCol;
        }

        var linePositionSpan = new LinePositionSpan(
            new LinePosition(startLine, startCol),
            new LinePosition(endLine, endCol));

        // Compute the character-offset span if we have the source text
        TextSpan textSpan;
        if (text != null && startLine < text.Lines.Count)
        {
            var startOffset = text.Lines.GetPosition(new LinePosition(startLine, startCol));

            int endOffset;
            if (endLine < text.Lines.Count)
            {
                endOffset = text.Lines.GetPosition(new LinePosition(endLine, endCol));
            }
            else
            {
                endOffset = startOffset;
            }

            textSpan = TextSpan.FromBounds(
                Math.Min(startOffset, text.Length),
                Math.Min(Math.Max(startOffset, endOffset), text.Length));
        }
        else
        {
            textSpan = new TextSpan(0, 0);
        }

        return Location.Create(filePath, textSpan, linePositionSpan);
    }

    private static string GetStringProperty(
        AnalyzerConfigOptions options,
        string key,
        string defaultValue)
    {
        return options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : defaultValue;
    }

    private static bool GetBoolProperty(
        AnalyzerConfigOptions options,
        string key,
        bool defaultValue)
    {
        if (options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return !value.Trim().Equals("false", StringComparison.OrdinalIgnoreCase);
        }

        return defaultValue;
    }
}
