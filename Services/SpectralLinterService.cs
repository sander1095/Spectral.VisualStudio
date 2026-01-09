using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Spectral.VisualStudio.Services;

/// <summary>
/// Service that invokes the Spectral CLI and parses results.
/// </summary>
public class SpectralLinterService
{
    private readonly TraceSource _logger;

    public SpectralLinterService(TraceSource logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Runs Spectral lint on the specified file.
    /// </summary>
    /// <param name="filePath">The file to lint.</param>
    /// <param name="spectralPath">Path to the Spectral CLI executable.</param>
    /// <param name="rulesetFile">Optional path to the ruleset file.</param>
    /// <param name="workingDirectory">The working directory for the command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of diagnostics found.</returns>
    public async Task<IReadOnlyList<SpectralDiagnostic>> LintFileAsync(
        string filePath,
        string spectralPath,
        string? rulesetFile,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            var arguments = BuildArguments(filePath, rulesetFile);
            var (exitCode, output, error) = await RunSpectralAsync(spectralPath, arguments, workingDirectory, cancellationToken);

            if (exitCode != 0 && exitCode != 1) // Exit code 1 means linting errors found, which is expected
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    _logger.TraceEvent(TraceEventType.Error, 0, $"Spectral error: {error}");
                }

                // If spectral isn't installed or has issues, return empty
                if (error.Contains("not recognized") || error.Contains("not found") || error.Contains("ENOENT"))
                {
                    _logger.TraceEvent(TraceEventType.Warning, 0, "Spectral CLI not found. Please install it using: npm install -g @stoplight/spectral-cli");
                    return Array.Empty<SpectralDiagnostic>();
                }
            }

            return ParseSpectralOutput(output, filePath);
        }
        catch (Exception ex)
        {
            _logger.TraceEvent(TraceEventType.Error, 0, $"Failed to run Spectral: {ex.Message}");
            return Array.Empty<SpectralDiagnostic>();
        }
    }

    /// <summary>
    /// Checks if a file matches the configured glob patterns.
    /// </summary>
    public bool ShouldLintFile(string filePath, string[] patterns, string workspaceRoot)
    {
        if (patterns == null || patterns.Length == 0)
        {
            return true;
        }

        // Get relative path
        var relativePath = GetRelativePath(workspaceRoot, filePath).Replace("\\", "/");

        bool matchesInclude = false;
        bool matchesExclude = false;
        bool hasIncludePatterns = false;

        foreach (var pattern in patterns)
        {
            if (pattern.StartsWith("!"))
            {
                var excludePattern = pattern.Substring(1);
                if (MatchGlob(relativePath, excludePattern))
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

        // If no include patterns, assume all files are included
        if (!hasIncludePatterns)
        {
            matchesInclude = true;
        }

        return matchesInclude && !matchesExclude;
    }

    private static string GetRelativePath(string basePath, string fullPath)
    {
        var baseUri = new Uri(basePath.EndsWith("\\") ? basePath : basePath + "\\");
        var fullUri = new Uri(fullPath);
        return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString());
    }

    private static bool MatchGlob(string path, string pattern)
    {
        // Convert glob pattern to regex
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^/]*")
            .Replace("\\?", ".") + "$";

        return Regex.IsMatch(path, regexPattern, RegexOptions.IgnoreCase);
    }

    private static string BuildArguments(string filePath, string? rulesetFile)
    {
        var args = $"lint \"{filePath}\" --format json";

        if (!string.IsNullOrWhiteSpace(rulesetFile))
        {
            args += $" --ruleset \"{rulesetFile}\"";
        }

        return args;
    }

    private static async Task<(int exitCode, string output, string error)> RunSpectralAsync(
        string spectralPath,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<int>();

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

        process.EnableRaisingEvents = true;
        process.Exited += (sender, e) => tcs.TrySetResult(process.ExitCode);

        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        using (cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch { }
            tcs.TrySetCanceled();
        }))
        {
            await tcs.Task;
        }

        var output = await outputTask;
        var error = await errorTask;

        return (process.ExitCode, output, error);
    }

    private static string GetExecutableName(string spectralPath)
    {
        // On Windows, we need to use cmd to run npm-installed global packages
        if (spectralPath == "spectral" || !Path.IsPathRooted(spectralPath))
        {
            return "cmd.exe";
        }

        return spectralPath;
    }

    private static string GetFullArguments(string spectralPath, string arguments)
    {
        // On Windows, wrap the command for cmd.exe
        if (spectralPath == "spectral" || !Path.IsPathRooted(spectralPath))
        {
            return $"/c {spectralPath} {arguments}";
        }

        return arguments;
    }

    private IReadOnlyList<SpectralDiagnostic> ParseSpectralOutput(string output, string filePath)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return Array.Empty<SpectralDiagnostic>();
        }

        try
        {
            var results = JsonConvert.DeserializeObject<SpectralCliResult[]>(output);

            if (results == null)
            {
                return Array.Empty<SpectralDiagnostic>();
            }

            return results.Select(r => new SpectralDiagnostic
            {
                FilePath = r.Source ?? filePath,
                Code = r.Code ?? string.Empty,
                Message = r.Message ?? string.Empty,
                Severity = r.Severity,
                StartLine = r.Range?.Start?.Line ?? 0,
                StartColumn = r.Range?.Start?.Character ?? 0,
                EndLine = r.Range?.End?.Line ?? 0,
                EndColumn = r.Range?.End?.Character ?? 0,
                Path = string.Join(".", r.Path ?? Array.Empty<string>()),
            }).ToList();
        }
        catch (JsonException ex)
        {
            _logger.TraceEvent(TraceEventType.Error, 0, $"Failed to parse Spectral output: {ex.Message}");
            return Array.Empty<SpectralDiagnostic>();
        }
    }
}
