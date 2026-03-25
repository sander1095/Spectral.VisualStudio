// RS1035 is suppressed because this service intentionally spawns the Spectral CLI
// (a Node.js process) as an external tool – there is no in-process alternative.
#pragma warning disable RS1035 // Do not use APIs banned for use by analyzers

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;

namespace Spectral.Net.Analyzer.Services;

/// <summary>
/// Invokes the Spectral CLI synchronously and parses the JSON output into
/// <see cref="SpectralDiagnostic"/> objects.
/// </summary>
public sealed class SpectralLinterService
{
    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Runs Spectral lint on <paramref name="filePath"/> and returns the parsed diagnostics.
    /// Returns an empty list (not an error) when Spectral is not installed, so the build
    /// does not break just because the CLI is missing.
    /// </summary>
    /// <param name="filePath">Absolute path of the file to lint.</param>
    /// <param name="spectralPath">
    ///   Path/name of the Spectral executable (default: <c>spectral</c>).
    /// </param>
    /// <param name="rulesetFile">Optional ruleset file path or URL.</param>
    /// <param name="workingDirectory">Directory to run Spectral in.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    ///   A <see cref="LintResult"/> containing the diagnostics or an error message.
    /// </returns>
    public LintResult LintFile(
        string filePath,
        string spectralPath,
        string? rulesetFile,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            // Auto-detect ruleset file if not explicitly provided
            var resolvedRuleset = string.IsNullOrWhiteSpace(rulesetFile)
                ? FindDefaultRulesetFile(workingDirectory)
                : rulesetFile;

            var arguments = BuildArguments(filePath, resolvedRuleset);
            var (exitCode, output, error) = RunSpectral(spectralPath, arguments, workingDirectory, cancellationToken);

            // Exit code 1 = linting found issues (expected); anything else is a tool error
            if (exitCode != 0 && exitCode != 1)
            {
                if (IsCliNotFound(error))
                {
                    return LintResult.Failure(
                        "Spectral CLI not found. Install it with: npm install -g @stoplight/spectral-cli");
                }

                if (!string.IsNullOrWhiteSpace(error))
                {
                    return LintResult.Failure($"Spectral exited with code {exitCode}: {error.Trim()}");
                }
            }

            var diagnostics = ParseSpectralOutput(output, filePath);
            return LintResult.Success(diagnostics);
        }
        catch (OperationCanceledException)
        {
            return LintResult.Success([]);
        }
        catch (Exception ex)
        {
            return LintResult.Failure($"Failed to run Spectral: {ex.Message}");
        }
    }

    /// <summary>
    /// Determines whether <paramref name="filePath"/> matches any of the include glob
    /// <paramref name="patterns"/>. Patterns prefixed with <c>!</c> are exclusions.
    /// </summary>
    public bool ShouldLintFile(string filePath, string[] patterns, string workspaceRoot)
    {
        if (patterns == null || patterns.Length == 0)
        {
            return true;
        }

        var relativePath = GetRelativePath(workspaceRoot, filePath).Replace("\\", "/");

        bool matchesInclude = false;
        bool matchesExclude = false;
        bool hasIncludePatterns = false;

        foreach (var pattern in patterns)
        {
            if (pattern.StartsWith("!", StringComparison.Ordinal))
            {
                if (MatchGlob(relativePath, pattern.Substring(1)))
                {
                    matchesExclude = true;
                }
            }
            else
            {
                hasIncludePatterns = true;
                if (MatchGlob(relativePath, pattern))
                {
                    matchesInclude = true;
                }
            }
        }

        if (!hasIncludePatterns)
        {
            matchesInclude = true;
        }

        return matchesInclude && !matchesExclude;
    }

    // -------------------------------------------------------------------------
    // Internal helpers (internal for unit testing)
    // -------------------------------------------------------------------------

    private static readonly string[] DefaultRulesetFileNames =
    [
        ".spectral.yaml",
        ".spectral.yml",
        ".spectral.json",
        ".spectral.js",
    ];

    internal static string? FindDefaultRulesetFile(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        foreach (var name in DefaultRulesetFileNames)
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    internal static IReadOnlyList<SpectralDiagnostic> ParseSpectralOutput(string output, string filePath)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        try
        {
            var results = JsonConvert.DeserializeObject<SpectralCliResult[]>(output);

            if (results == null)
            {
                return [];
            }

            return results.Select(r => new SpectralDiagnostic
            {
                FilePath = string.IsNullOrWhiteSpace(r.Source) ? filePath : r.Source,
                Code = r.Code ?? string.Empty,
                Message = r.Message ?? string.Empty,
                Severity = r.Severity,
                StartLine = r.Range?.Start?.Line ?? 0,
                StartColumn = r.Range?.Start?.Character ?? 0,
                EndLine = r.Range?.End?.Line ?? 0,
                EndColumn = r.Range?.End?.Character ?? 0,
                Path = r.Path != null ? string.Join(".", r.Path) : string.Empty,
            }).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static string BuildArguments(string filePath, string? rulesetFile)
    {
        var args = $"lint \"{filePath}\" --format json";

        if (!string.IsNullOrWhiteSpace(rulesetFile))
        {
            args += $" --ruleset \"{rulesetFile}\"";
        }

        return args;
    }

    private static (int exitCode, string output, string error) RunSpectral(
        string spectralPath,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = GetExecutableName(spectralPath),
            Arguments = GetFullArguments(spectralPath, arguments),
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        process.Start();

        // Read output in separate threads to avoid deadlocks on full buffers
        var outputTask = System.Threading.Tasks.Task.Run(
            () => process.StandardOutput.ReadToEnd(), cancellationToken);
        var errorTask = System.Threading.Tasks.Task.Run(
            () => process.StandardError.ReadToEnd(), cancellationToken);

        // Wait for process (with cancellation support)
        if (!process.WaitForExit(60_000))
        {
            try { process.Kill(); } catch { /* best-effort */ }
            throw new TimeoutException("Spectral timed out after 60 seconds.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(); } catch { /* best-effort */ }
            cancellationToken.ThrowIfCancellationRequested();
        }

        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();

        return (process.ExitCode, output, error);
    }

    private static string GetExecutableName(string spectralPath)
    {
        // On Windows, npm-installed global packages need to be invoked via cmd.exe
        if (IsWindows() && !Path.IsPathRooted(spectralPath))
        {
            return "cmd.exe";
        }

        return spectralPath;
    }

    private static string GetFullArguments(string spectralPath, string arguments)
    {
        if (IsWindows() && !Path.IsPathRooted(spectralPath))
        {
            return $"/c {spectralPath} {arguments}";
        }

        return arguments;
    }

    private static bool IsWindows()
    {
#if NETSTANDARD2_0
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#else
        return OperatingSystem.IsWindows();
#endif
    }

    private static bool IsCliNotFound(string error)
    {
        return error.Contains("not recognized", StringComparison.OrdinalIgnoreCase)
            || error.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || error.Contains("ENOENT", StringComparison.OrdinalIgnoreCase)
            || error.Contains("No such file", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRelativePath(string basePath, string fullPath)
    {
        var baseUri = new Uri(basePath.EndsWith(Path.DirectorySeparatorChar.ToString(),
            StringComparison.Ordinal) ? basePath : basePath + Path.DirectorySeparatorChar);
        var fullUri = new Uri(fullPath);
        return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString());
    }

    private static bool MatchGlob(string path, string pattern)
    {
        // Convert glob pattern to regex.
        // Order matters: handle **/ before ** before *.
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*\\*/", "(.*/)?")  // **/ = zero or more directory levels (including none)
            .Replace("\\*\\*", ".*")         // ** alone = any characters including /
            .Replace("\\*", "[^/]*")          // * = any characters except /
            .Replace("\\?", ".") + "$";

        return Regex.IsMatch(path, regexPattern, RegexOptions.IgnoreCase);
    }
}

/// <summary>The result of a <see cref="SpectralLinterService.LintFile"/> call.</summary>
public sealed class LintResult
{
    /// <summary>Diagnostics found (empty on error).</summary>
    public IReadOnlyList<SpectralDiagnostic> Diagnostics { get; }

    /// <summary><c>false</c> when the Spectral CLI could not be executed.</summary>
    public bool IsSuccess { get; }

    /// <summary>Human-readable error message when <see cref="IsSuccess"/> is <c>false</c>.</summary>
    public string? ErrorMessage { get; }

    private LintResult(bool isSuccess, IReadOnlyList<SpectralDiagnostic> diagnostics, string? errorMessage)
    {
        IsSuccess = isSuccess;
        Diagnostics = diagnostics;
        ErrorMessage = errorMessage;
    }

    internal static LintResult Success(IReadOnlyList<SpectralDiagnostic> diagnostics) =>
        new(true, diagnostics, null);

    internal static LintResult Failure(string errorMessage) =>
        new(false, [], errorMessage);
}
