#pragma warning disable VSEXTPREVIEW_SETTINGS // The settings API is currently in preview

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;
using Microsoft.VisualStudio.Extensibility.VSSdkCompatibility;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Spectral.VisualStudio.ErrorList;
using Spectral.VisualStudio.Services;
using Spectral.VisualStudio.Settings;

namespace Spectral.VisualStudio;

/// <summary>
/// Command to lint the current file with Spectral.
/// </summary>
[VisualStudioContribution]
internal class LintCurrentFileCommand : Command
{
    private readonly TraceSource _logger;
    private readonly VisualStudioExtensibility _extensibility;
    private readonly AsyncServiceProviderInjection<SVsSolution, IVsSolution> _solutionService;
    private readonly MefInjection<SVsServiceProvider> _serviceProviderInjection;
    private SpectralErrorListManager? _errorListManager;
    private IServiceProvider? _serviceProvider;

    public LintCurrentFileCommand(
        VisualStudioExtensibility extensibility,
        TraceSource traceSource,
        AsyncServiceProviderInjection<SVsSolution, IVsSolution> solutionService,
        MefInjection<SVsServiceProvider> serviceProviderInjection)
    {
        _extensibility = extensibility;
        _logger = Requires.NotNull(traceSource, nameof(traceSource));
        _solutionService = solutionService;
        _serviceProviderInjection = serviceProviderInjection;
    }

    /// <inheritdoc />
    public override CommandConfiguration CommandConfiguration => new("%Spectral.VisualStudio.Command1.DisplayName%")
    {
        Icon = new(ImageMoniker.KnownValues.StatusOK, IconSettings.IconAndText),
        Placements = new[] { CommandPlacement.KnownPlacements.ExtensionsMenu },
    };

    /// <inheritdoc />
    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        try
        {
            // Get the active document
            var activeDocument = await context.GetActiveTextViewAsync(cancellationToken);
            if (activeDocument == null)
            {
                await _extensibility.Shell().ShowPromptAsync(
                    "No file is currently open. Please open a file to lint.",
                    PromptOptions.OK,
                    cancellationToken);
                return;
            }

            var filePath = activeDocument.Uri?.LocalPath;
            if (string.IsNullOrEmpty(filePath))
            {
                await _extensibility.Shell().ShowPromptAsync(
                    "Unable to determine the file path of the current document.",
                    PromptOptions.OK,
                    cancellationToken);
                return;
            }

            // Read settings
            var settings = _extensibility.Settings();
            var enableResult = await settings.ReadEffectiveValueAsync(SpectralSettingsDefinitions.Enable, cancellationToken);
            var enabled = enableResult.ValueOrDefault(defaultValue: true);

            if (!enabled)
            {
                await _extensibility.Shell().ShowPromptAsync(
                    "Spectral linting is disabled. Enable it in Tools > Options > Spectral Linter.",
                    PromptOptions.OK,
                    cancellationToken);
                return;
            }

            var spectralPathResult = await settings.ReadEffectiveValueAsync(SpectralSettingsDefinitions.SpectralPath, cancellationToken);
            var spectralPath = spectralPathResult.ValueOrDefault(defaultValue: "spectral");

            var rulesetFileResult = await settings.ReadEffectiveValueAsync(SpectralSettingsDefinitions.RulesetFile, cancellationToken);
            var rulesetFile = rulesetFileResult.ValueOrDefault(defaultValue: "");

            // Get working directory
            var workingDirectory = await GetWorkingDirectoryAsync(filePath!, cancellationToken);

            // Resolve ruleset file
            var resolvedRulesetFile = ResolveRulesetFile(rulesetFile, workingDirectory);

            // Ensure we have the service provider and error list manager
            await EnsureErrorListManagerAsync(cancellationToken);

            // Run the linter
            var linterService = new SpectralLinterService(_logger);
            var diagnostics = await linterService.LintFileAsync(
                filePath!,
                spectralPath,
                resolvedRulesetFile,
                workingDirectory,
                cancellationToken);

            // Update error list on UI thread
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            _errorListManager!.ClearErrorsForFile(filePath!);

            if (diagnostics.Count > 0)
            {
                _errorListManager.AddDiagnostics(diagnostics);
                _errorListManager.Show();
            }

            await _extensibility.Shell().ShowPromptAsync(
                $"Spectral found {diagnostics.Count} issue(s) in {Path.GetFileName(filePath)}.",
                PromptOptions.OK,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.TraceEvent(TraceEventType.Error, 0, $"Error during linting: {ex.Message}");
            await _extensibility.Shell().ShowPromptAsync(
                $"An error occurred during linting: {ex.Message}",
                PromptOptions.OK,
                cancellationToken);
        }
    }

    private async Task EnsureErrorListManagerAsync(CancellationToken cancellationToken)
    {
        if (_errorListManager == null)
        {
            _serviceProvider = await _serviceProviderInjection.GetServiceAsync() as IServiceProvider;
            if (_serviceProvider != null)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                _errorListManager = new SpectralErrorListManager(_serviceProvider, _logger);
            }
        }
    }

    private async Task<string> GetWorkingDirectoryAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            var solution = await _solutionService.GetServiceAsync();
            if (solution != null && solution.GetSolutionInfo(out string solutionDir, out _, out _) == Microsoft.VisualStudio.VSConstants.S_OK)
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

        return Path.GetDirectoryName(filePath) ?? Environment.CurrentDirectory;
    }

    private static string? ResolveRulesetFile(string? rulesetFile, string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(rulesetFile))
        {
            var defaultFiles = new[]
            {
                ".spectral.yaml",
                ".spectral.yml",
                ".spectral.json",
                ".spectral.js",
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

        // At this point rulesetFile is not null or whitespace
        if (rulesetFile!.StartsWith("http://") || rulesetFile.StartsWith("https://"))
        {
            return rulesetFile;
        }

        if (!Path.IsPathRooted(rulesetFile))
        {
            return Path.Combine(workingDirectory, rulesetFile);
        }

        return rulesetFile;
    }
}
