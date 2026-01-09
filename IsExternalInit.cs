// Polyfills for C# 9+ and .NET features on .NET Framework
// These types are required by the compiler when using modern C# features

#if NETFRAMEWORK

namespace System.Runtime.CompilerServices
{
    using System.ComponentModel;

    /// <summary>
    /// Reserved to be used by the compiler for tracking metadata.
    /// This class should not be used by developers in source code.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit
    {
    }
}

namespace System.Diagnostics.CodeAnalysis
{
    using System;

    /// <summary>
    /// Indicates that an API is experimental and it may change in the future.
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Assembly |
        AttributeTargets.Module |
        AttributeTargets.Class |
        AttributeTargets.Struct |
        AttributeTargets.Enum |
        AttributeTargets.Constructor |
        AttributeTargets.Method |
        AttributeTargets.Property |
        AttributeTargets.Field |
        AttributeTargets.Event |
        AttributeTargets.Interface |
        AttributeTargets.Delegate,
        Inherited = false)]
    internal sealed class ExperimentalAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ExperimentalAttribute"/> class.
        /// </summary>
        /// <param name="diagnosticId">The ID that the compiler will use when reporting a use of the API.</param>
        public ExperimentalAttribute(string diagnosticId)
        {
            DiagnosticId = diagnosticId;
        }

        /// <summary>
        /// Gets the ID that the compiler will use when reporting a use of the API.
        /// </summary>
        public string DiagnosticId { get; }

        /// <summary>
        /// Gets or sets the URL for corresponding documentation.
        /// </summary>
        public string? UrlFormat { get; set; }
    }
}

#endif
