namespace Spectral.Net.Analyzer.Services;

/// <summary>
/// Represents a single diagnostic reported by the Spectral CLI.
/// </summary>
public sealed class SpectralDiagnostic
{
    /// <summary>Absolute path of the linted file.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Human-readable diagnostic message.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Spectral rule code that triggered this diagnostic.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Severity level as reported by Spectral:
    /// 0 = Error, 1 = Warning, 2 = Info, 3 = Hint.
    /// </summary>
    public int Severity { get; set; }

    /// <summary>0-based start line.</summary>
    public int StartLine { get; set; }

    /// <summary>0-based start column.</summary>
    public int StartColumn { get; set; }

    /// <summary>0-based end line.</summary>
    public int EndLine { get; set; }

    /// <summary>0-based end column.</summary>
    public int EndColumn { get; set; }

    /// <summary>JSON-path to the offending element (e.g. "paths./users.get.operationId").</summary>
    public string Path { get; set; } = string.Empty;
}

/// <summary>Raw JSON result item returned by <c>spectral lint --format json</c>.</summary>
public sealed class SpectralCliResult
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string[] Path { get; set; } = [];
    public int Severity { get; set; }
    public SpectralRange? Range { get; set; }
    public string Source { get; set; } = string.Empty;
}

/// <summary>Line/column range of a diagnostic.</summary>
public sealed class SpectralRange
{
    public SpectralPosition? Start { get; set; }
    public SpectralPosition? End { get; set; }
}

/// <summary>A single position (line + character, both 0-based).</summary>
public sealed class SpectralPosition
{
    public int Line { get; set; }
    public int Character { get; set; }
}
