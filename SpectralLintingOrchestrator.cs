#pragma warning disable VSEXTPREVIEW_SETTINGS // The settings API is currently in preview

using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.VSSdkCompatibility;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Spectral.VisualStudio.ErrorList;
using Spectral.VisualStudio.Services;
using Spectral.VisualStudio.Settings;

namespace Spectral.VisualStudio;

/// <summary>
/// Orchestrates Spectral linting operations within Visual Studio.
/// </summary>
public class SpectralLintingOrchestrator : IDisposable
{
    private readonly VisualStudioExtensibility _extensibility;
    private readonly TraceSource _logger;
    private readonly SpectralLinterService _linterService;
    private SpectralErrorListManager? _errorListManager;
    private readonly AsyncServiceProviderInjection<SVsSolution, IVsSolution> _solutionService;
    private readonly IServiceProvider _serviceProvider;
    private bool _disposed;

    public SpectralLintingOrchestrator(
        VisualStudioExtensibility extensibility,
        TraceSource logger,
        AsyncServiceProviderInjection<SVsSolution, IVsSolution> solutionService,
        MefInjection<SVsServiceProvider> serviceProviderInjection)
    {
        _extensibility = extensibility;
        _logger = logger;
        _solutionService = solutionService;
        _linterService = new SpectralLinterService(logger);

        // Get the service provider synchronously since we need it for ErrorListProvider
        _serviceProvider = ThreadHelper.JoinableTaskFactory.Run(async () =>
        {
            return await serviceProviderInjection.GetServiceAsync() as IServiceProvider
                ?? throw new InvalidOperationException("Failed to get service provider");
        });
    }

    private SpectralErrorListManager ErrorListManager
    {
        get
        {
            if (_errorListManager == null)
            {
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    _errorListManager = new SpectralErrorListManager(_serviceProvider, _logger);
                });
            }
            return _errorListManager!;
        }
    }

    /// <summary>
    /// Lints a single file and updates the error list.
    /// </summary>
    public async Task LintFileAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            // Read settings
            var settings = _extensibility.Settings();
            var enableResult = await settings.ReadEffectiveValueAsync(SpectralSettingsDefinitions.Enable, cancellationToken);
            var enabled = enableResult.ValueOrDefault(defaultValue: true);

            if (!enabled)
            {
                _logger.TraceEvent(TraceEventType.Information, 0, "Spectral linting is disabled");
                return;
            }

            var spectralPathResult = await settings.ReadEffectiveValueAsync(SpectralSettingsDefinitions.SpectralPath, cancellationToken);
            var spectralPath = spectralPathResult.ValueOrDefault(defaultValue: "spectral");

            var rulesetFileResult = await settings.ReadEffectiveValueAsync(SpectralSettingsDefinitions.RulesetFile, cancellationToken);
            var rulesetFile = rulesetFileResult.ValueOrDefault(defaultValue: "");

            var validateFilesResult = await settings.ReadEffectiveValueAsync(SpectralSettingsDefinitions.ValidateFiles, cancellationToken);
            var validateFiles = validateFilesResult.ValueOrDefault(defaultValue: new[] { "**/*.yaml", "**/*.yml", "**/*.json" });

            // Get working directory (solution directory or file directory)
            var workingDirectory = await GetWorkingDirectoryAsync(filePath, cancellationToken);

            // Check if file matches patterns
            if (!_linterService.ShouldLintFile(filePath, validateFiles, workingDirectory))
            {
                _logger.TraceEvent(TraceEventType.Information, 0, $"File {filePath} does not match configured patterns");
                return;
            }

            // Resolve ruleset file path if relative
            var resolvedRulesetFile = ResolveRulesetFile(rulesetFile, workingDirectory);

            // Run the linter
            _logger.TraceEvent(TraceEventType.Information, 0, $"Linting file: {filePath}");
            var diagnostics = await _linterService.LintFileAsync(
                filePath,
                spectralPath,
                resolvedRulesetFile,
                workingDirectory,
                cancellationToken);

            // Update error list on UI thread
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            ErrorListManager.ClearErrorsForFile(filePath);
            ErrorListManager.AddDiagnostics(diagnostics);

            _logger.TraceEvent(TraceEventType.Information, 0, $"Found {diagnostics.Count} issues in {filePath}");
        }
        catch (OperationCanceledException)
        {
            _logger.TraceEvent(TraceEventType.Information, 0, "Linting cancelled");
        }
        catch (Exception ex)
        {
            _logger.TraceEvent(TraceEventType.Error, 0, $"Error during linting: {ex.Message}");
        }
    }

    /// <summary>
    /// Clears all Spectral errors from the error list.
    /// </summary>
    public async Task ClearAllErrorsAsync(CancellationToken cancellationToken)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        ErrorListManager.ClearAllErrors();
    }

    private async Task<string> GetWorkingDirectoryAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            var solution = await _solutionService.GetServiceAsync();
            if (solution != null && solution.GetSolutionInfo(out string solutionDir, out _, out _) == VSConstants.S_OK)
            {
                if (!string.IsNullOrEmpty(solutionDir))
                {
                    return solutionDir;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.TraceEvent(TraceEventType.Warning, 0, $"Failed to get solution directory: {ex.Message}");
        }

        // Fall back to file's directory
        return Path.GetDirectoryName(filePath) ?? Environment.CurrentDirectory;
    }

    private static string? ResolveRulesetFile(string? rulesetFile, string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(rulesetFile))
        {
            // Try to find default ruleset files
            var defaultFiles = new[]
            {
                ".spectral.yaml",
                ".spectral.yml",
                ".spectral.json",
                ".spectral.js",
                "spectral.yaml",
                "spectral.yml",
                "spectral.json",
            };

            foreach (var defaultFile in defaultFiles)
            {
                var path = Path.Combine(workingDirectory, defaultFile);
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }

        // If it's a URL, return as-is (rulesetFile is not null/whitespace at this point)
        if (rulesetFile!.StartsWith("http://") || rulesetFile.StartsWith("https://"))
        {
            return rulesetFile;
        }

        // If it's a relative path, make it absolute
        if (!Path.IsPathRooted(rulesetFile))
        {
            return Path.Combine(workingDirectory, rulesetFile);
        }

        return rulesetFile;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _errorListManager?.Dispose();
            _disposed = true;
        }
    }
}
