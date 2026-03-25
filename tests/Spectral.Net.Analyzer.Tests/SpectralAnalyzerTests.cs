using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Spectral.Net.Analyzer;
using Xunit;

namespace Spectral.Net.Analyzer.Tests;

/// <summary>
/// Tests for <see cref="SpectralAnalyzer"/> metadata and descriptor configuration.
/// </summary>
public sealed class SpectralAnalyzerTests
{
    private readonly SpectralAnalyzer _analyzer = new();

    [Fact]
    public void SupportedDiagnostics_ContainsAllFiveDescriptors()
    {
        Assert.Equal(5, _analyzer.SupportedDiagnostics.Length);
    }

    [Theory]
    [InlineData("SPECTRAL000", DiagnosticSeverity.Warning)]
    [InlineData("SPECTRAL001", DiagnosticSeverity.Error)]
    [InlineData("SPECTRAL002", DiagnosticSeverity.Warning)]
    [InlineData("SPECTRAL003", DiagnosticSeverity.Info)]
    [InlineData("SPECTRAL004", DiagnosticSeverity.Info)]
    public void SupportedDiagnostics_HasExpectedIdAndSeverity(string id, DiagnosticSeverity severity)
    {
        var descriptor = FindDescriptor(_analyzer.SupportedDiagnostics, id);
        Assert.NotNull(descriptor);
        Assert.Equal(severity, descriptor!.DefaultSeverity);
        Assert.True(descriptor.IsEnabledByDefault);
        Assert.Equal("Spectral", descriptor.Category);
    }

    [Fact]
    public void Analyzer_SupportsLanguages_CSharpAndVisualBasic()
    {
        var languages = _analyzer.GetType()
            .GetCustomAttributes(typeof(DiagnosticAnalyzerAttribute), inherit: false);
        Assert.NotEmpty(languages);

        var attr = (DiagnosticAnalyzerAttribute)languages[0];
        Assert.Contains(LanguageNames.CSharp, attr.Languages);
        Assert.Contains(LanguageNames.VisualBasic, attr.Languages);
    }

    private static DiagnosticDescriptor? FindDescriptor(
        ImmutableArray<DiagnosticDescriptor> descriptors, string id)
    {
        foreach (var d in descriptors)
        {
            if (d.Id == id) return d;
        }
        return null;
    }
}
