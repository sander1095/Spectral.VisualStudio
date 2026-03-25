using System;
using System.Collections.Generic;
using Spectral.Net.Analyzer.Services;
using Xunit;

namespace Spectral.Net.Analyzer.Tests;

/// <summary>
/// Unit tests for <see cref="SpectralLinterService"/> focusing on pure parsing
/// and glob-matching logic that does not require the Spectral CLI to be installed.
/// </summary>
public sealed class SpectralLinterServiceTests
{
    // -------------------------------------------------------------------------
    // ParseSpectralOutput
    // -------------------------------------------------------------------------

    [Fact]
    public void ParseSpectralOutput_EmptyString_ReturnsEmpty()
    {
        var result = SpectralLinterService.ParseSpectralOutput("", "/file.yaml");
        Assert.Empty(result);
    }

    [Fact]
    public void ParseSpectralOutput_WhitespaceOnly_ReturnsEmpty()
    {
        var result = SpectralLinterService.ParseSpectralOutput("   ", "/file.yaml");
        Assert.Empty(result);
    }

    [Fact]
    public void ParseSpectralOutput_InvalidJson_ReturnsEmpty()
    {
        var result = SpectralLinterService.ParseSpectralOutput("not json", "/file.yaml");
        Assert.Empty(result);
    }

    [Fact]
    public void ParseSpectralOutput_NullJson_ReturnsEmpty()
    {
        var result = SpectralLinterService.ParseSpectralOutput("null", "/file.yaml");
        Assert.Empty(result);
    }

    [Fact]
    public void ParseSpectralOutput_EmptyArray_ReturnsEmpty()
    {
        var result = SpectralLinterService.ParseSpectralOutput("[]", "/file.yaml");
        Assert.Empty(result);
    }

    [Fact]
    public void ParseSpectralOutput_SingleError_MapsCorrectly()
    {
        const string json = """
            [
              {
                "code": "operation-operationId-unique",
                "message": "Every operation must have unique \"operationId\".",
                "path": ["paths", "/users", "get", "operationId"],
                "severity": 0,
                "range": {
                  "start": { "line": 10, "character": 4 },
                  "end":   { "line": 10, "character": 20 }
                },
                "source": "/workspace/openapi.yaml"
              }
            ]
            """;

        var result = SpectralLinterService.ParseSpectralOutput(json, "/fallback.yaml");

        var diag = Assert.Single(result);
        Assert.Equal("operation-operationId-unique", diag.Code);
        Assert.Equal("Every operation must have unique \"operationId\".", diag.Message);
        Assert.Equal(0, diag.Severity);
        Assert.Equal(10, diag.StartLine);
        Assert.Equal(4, diag.StartColumn);
        Assert.Equal(10, diag.EndLine);
        Assert.Equal(20, diag.EndColumn);
        Assert.Equal("/workspace/openapi.yaml", diag.FilePath);
        Assert.Equal("paths./users.get.operationId", diag.Path);
    }

    [Fact]
    public void ParseSpectralOutput_MissingSource_FallsBackToFilePath()
    {
        const string json = """
            [
              {
                "code": "some-rule",
                "message": "msg",
                "path": [],
                "severity": 1,
                "range": {
                  "start": { "line": 0, "character": 0 },
                  "end":   { "line": 0, "character": 0 }
                },
                "source": ""
              }
            ]
            """;

        var result = SpectralLinterService.ParseSpectralOutput(json, "/my/file.yaml");
        Assert.Equal("/my/file.yaml", Assert.Single(result).FilePath);
    }

    [Fact]
    public void ParseSpectralOutput_MultipleDiagnostics_ReturnsAll()
    {
        const string json = """
            [
              { "code": "rule-a", "message": "msg-a", "path": [], "severity": 0, "range": { "start": { "line": 1, "character": 0 }, "end": { "line": 1, "character": 5 } }, "source": "/f.yaml" },
              { "code": "rule-b", "message": "msg-b", "path": [], "severity": 1, "range": { "start": { "line": 2, "character": 0 }, "end": { "line": 2, "character": 5 } }, "source": "/f.yaml" },
              { "code": "rule-c", "message": "msg-c", "path": [], "severity": 2, "range": { "start": { "line": 3, "character": 0 }, "end": { "line": 3, "character": 5 } }, "source": "/f.yaml" }
            ]
            """;

        var result = SpectralLinterService.ParseSpectralOutput(json, "/f.yaml");
        Assert.Equal(3, result.Count);
        Assert.Equal(0, result[0].Severity);
        Assert.Equal(1, result[1].Severity);
        Assert.Equal(2, result[2].Severity);
    }

    [Fact]
    public void ParseSpectralOutput_NullPath_UsesEmptyString()
    {
        const string json = """
            [
              {
                "code": "some-rule",
                "message": "msg",
                "path": null,
                "severity": 1,
                "range": { "start": { "line": 0, "character": 0 }, "end": { "line": 0, "character": 0 } },
                "source": "/f.yaml"
              }
            ]
            """;

        var diag = Assert.Single(SpectralLinterService.ParseSpectralOutput(json, "/f.yaml"));
        Assert.Equal(string.Empty, diag.Path);
    }

    // -------------------------------------------------------------------------
    // ShouldLintFile
    // -------------------------------------------------------------------------

    private static readonly SpectralLinterService _svc = new();
    private const string Root = "/workspace";

    [Theory]
    [InlineData("/workspace/openapi.yaml", new[] { "**/*.yaml" }, true)]
    [InlineData("/workspace/openapi.json", new[] { "**/*.yaml" }, false)]
    [InlineData("/workspace/openapi.yaml", new[] { "**/*.yaml", "!**/secret.yaml" }, true)]
    [InlineData("/workspace/secret.yaml", new[] { "**/*.yaml", "!**/secret.yaml" }, false)]
    [InlineData("/workspace/api/v1.yaml", new[] { "**/*.yaml" }, true)]
    public void ShouldLintFile_VariousPatterns_MatchesExpected(
        string filePath, string[] patterns, bool expected)
    {
        Assert.Equal(expected, _svc.ShouldLintFile(filePath, patterns, Root));
    }

    [Fact]
    public void ShouldLintFile_EmptyPatterns_ReturnsTrue()
    {
        Assert.True(_svc.ShouldLintFile("/workspace/openapi.yaml", [], Root));
    }
}
