namespace Spectral.VisualStudio.Services;

/// <summary>
/// Represents a diagnostic result from Spectral linting.
/// </summary>
public class SpectralDiagnostic
{
    /// <summary>
    /// The path to the source file.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// The diagnostic message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// The rule code that triggered this diagnostic.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// The severity level (0 = Error, 1 = Warning, 2 = Info, 3 = Hint).
    /// </summary>
    public int Severity { get; set; }

    /// <summary>
    /// The starting line number (0-based).
    /// </summary>
    public int StartLine { get; set; }

    /// <summary>
    /// The starting column number (0-based).
    /// </summary>
    public int StartColumn { get; set; }

    /// <summary>
    /// The ending line number (0-based).
    /// </summary>
    public int EndLine { get; set; }

    /// <summary>
    /// The ending column number (0-based).
    /// </summary>
    public int EndColumn { get; set; }

    /// <summary>
    /// The JSON path to the problematic element.
    /// </summary>
    public string Path { get; set; } = string.Empty;
}

/// <summary>
/// Represents the raw JSON output from Spectral CLI.
/// </summary>
public class SpectralCliResult
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string[] Path { get; set; } = [];
    public int Severity { get; set; }
    public SpectralRange? Range { get; set; }
    public string Source { get; set; } = string.Empty;
}

public class SpectralRange
{
    public SpectralPosition? Start { get; set; }
    public SpectralPosition? End { get; set; }
}

public class SpectralPosition
{
    public int Line { get; set; }
    public int Character { get; set; }
}
