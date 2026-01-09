#pragma warning disable VSEXTPREVIEW_SETTINGS // The settings API is currently in preview

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Settings;

namespace Spectral.VisualStudio.Settings;

/// <summary>
/// Defines the Spectral extension settings.
/// </summary>
internal static class SpectralSettingsDefinitions
{
    /// <summary>
    /// The main Spectral settings category.
    /// </summary>
    [VisualStudioContribution]
    internal static SettingCategory SpectralCategory { get; } = new("spectral", "%Spectral.Settings.Category%")
    {
        Description = "%Spectral.Settings.Category.Description%",
        GenerateObserverClass = true,
    };

    /// <summary>
    /// Controls whether Spectral linting is enabled.
    /// </summary>
    [VisualStudioContribution]
    internal static Setting.Boolean Enable { get; } = new(
        "enable",
        "%Spectral.Settings.Enable%",
        SpectralCategory,
        defaultValue: true)
    {
        Description = "%Spectral.Settings.Enable.Description%",
    };

    /// <summary>
    /// Path to the ruleset file.
    /// </summary>
    [VisualStudioContribution]
    internal static Setting.String RulesetFile { get; } = new(
        "rulesetFile",
        "%Spectral.Settings.RulesetFile%",
        SpectralCategory,
        defaultValue: "")
    {
        Description = "%Spectral.Settings.RulesetFile.Description%",
    };

    /// <summary>
    /// When to run the linter.
    /// </summary>
    [VisualStudioContribution]
    internal static Setting.Enum RunMode { get; } = new(
        "runMode",
        "%Spectral.Settings.RunMode%",
        SpectralCategory,
        new EnumSettingEntry[]
        {
            new("onSave", "%Spectral.Settings.RunMode.OnSave%"),
            new("onType", "%Spectral.Settings.RunMode.OnType%"),
        },
        defaultValue: "onSave")
    {
        Description = "%Spectral.Settings.RunMode.Description%",
    };

    /// <summary>
    /// Glob patterns for files to validate.
    /// </summary>
    [VisualStudioContribution]
    internal static Setting.StringArray ValidateFiles { get; } = new(
        "validateFiles",
        "%Spectral.Settings.ValidateFiles%",
        SpectralCategory,
        defaultValue: new[] { "**/*.yaml", "**/*.yml", "**/*.json" })
    {
        Description = "%Spectral.Settings.ValidateFiles.Description%",
    };

    /// <summary>
    /// Path to the Spectral CLI executable.
    /// </summary>
    [VisualStudioContribution]
    internal static Setting.String SpectralPath { get; } = new(
        "spectralPath",
        "%Spectral.Settings.SpectralPath%",
        SpectralCategory,
        defaultValue: "spectral")
    {
        Description = "%Spectral.Settings.SpectralPath.Description%",
    };
}
