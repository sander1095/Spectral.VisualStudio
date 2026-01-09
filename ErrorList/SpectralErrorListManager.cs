using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Spectral.VisualStudio.Services;

namespace Spectral.VisualStudio.ErrorList;

/// <summary>
/// Manages the Visual Studio Error List integration for Spectral diagnostics.
/// </summary>
public class SpectralErrorListManager : IDisposable
{
    private readonly ErrorListProvider _errorListProvider;
    private readonly TraceSource _logger;
    private bool _disposed;

    public SpectralErrorListManager(IServiceProvider serviceProvider, TraceSource logger)
    {
        _logger = logger;
        _errorListProvider = new ErrorListProvider(serviceProvider)
        {
            ProviderName = "Spectral Linter",
            ProviderGuid = new Guid("E8F2A3B4-5C6D-7E8F-9A0B-1C2D3E4F5A6B"),
        };
    }

    /// <summary>
    /// Clears all Spectral errors from the error list.
    /// </summary>
    public void ClearAllErrors()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _errorListProvider.Tasks.Clear();
    }

    /// <summary>
    /// Clears errors for a specific file.
    /// </summary>
    public void ClearErrorsForFile(string filePath)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var tasksToRemove = new List<ErrorTask>();

        foreach (ErrorTask task in _errorListProvider.Tasks)
        {
            if (string.Equals(task.Document, filePath, StringComparison.OrdinalIgnoreCase))
            {
                tasksToRemove.Add(task);
            }
        }

        foreach (var task in tasksToRemove)
        {
            _errorListProvider.Tasks.Remove(task);
        }
    }

    /// <summary>
    /// Adds diagnostics to the error list.
    /// </summary>
    public void AddDiagnostics(IEnumerable<SpectralDiagnostic> diagnostics)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        foreach (var diagnostic in diagnostics)
        {
            var errorTask = new ErrorTask
            {
                Category = TaskCategory.CodeSense,
                ErrorCategory = MapSeverity(diagnostic.Severity),
                Text = FormatMessage(diagnostic),
                Document = diagnostic.FilePath,
                Line = diagnostic.StartLine,
                Column = diagnostic.StartColumn,
                HierarchyItem = null,
            };

            errorTask.Navigate += ErrorTask_Navigate;

            _errorListProvider.Tasks.Add(errorTask);
        }
    }

    /// <summary>
    /// Shows the error list window.
    /// </summary>
    public void Show()
    {
        _errorListProvider.Show();
    }

    /// <summary>
    /// Brings the error list to the front.
    /// </summary>
    public void BringToFront()
    {
        _errorListProvider.BringToFront();
    }

    private void ErrorTask_Navigate(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (sender is ErrorTask task)
        {
            _errorListProvider.Navigate(task, Guid.Empty);
        }
    }

    private static TaskErrorCategory MapSeverity(int severity)
    {
        // Spectral severity: 0 = Error, 1 = Warning, 2 = Info, 3 = Hint
        return severity switch
        {
            0 => TaskErrorCategory.Error,
            1 => TaskErrorCategory.Warning,
            _ => TaskErrorCategory.Message,
        };
    }

    private static string FormatMessage(SpectralDiagnostic diagnostic)
    {
        var message = diagnostic.Message;

        if (!string.IsNullOrEmpty(diagnostic.Code))
        {
            message = $"[{diagnostic.Code}] {message}";
        }

        if (!string.IsNullOrEmpty(diagnostic.Path))
        {
            message = $"{message} (path: {diagnostic.Path})";
        }

        return message;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _errorListProvider.Dispose();
            _disposed = true;
        }
    }
}
