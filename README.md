# Spectral.Net.Analyzer

A **.NET Roslyn Analyzer** that integrates the [Spectral](https://stoplight.io/open-source/spectral) API linter directly into your `dotnet build` pipeline. Spectral rule violations are surfaced as standard build diagnostics — errors, warnings, and informational messages — in any IDE or CI environment.

> **Migrated from a Visual Studio Extension.**  
> The project was previously a VS-only extension that showed results in the Error List. The Roslyn Analyzer approach provides deeper integration: results appear in `dotnet build` output, MSBuild logs, and any IDE that supports Roslyn (Visual Studio, VS Code with C# Dev Kit, JetBrains Rider, etc.).

---

## Features

- 🔍 Runs Spectral against your YAML / JSON API specification files at **build time**
- 🚦 Reports `error`, `warning`, and `info` diagnostics that map to Spectral's severity levels
- 🔗 File locations link directly to the offending line/column in your editor
- ⚙️ Fully configurable via MSBuild properties
- 🌍 Works in any IDE, CLI (`dotnet build`), and CI environment
- 📦 Distributed as a NuGet package with source-link support

---

## Prerequisites

- .NET 5 SDK or later
- [Spectral CLI](https://docs.stoplight.io/docs/spectral/b8391e051b7d8-installation) installed and available on your `PATH`:

  ```bash
  npm install -g @stoplight/spectral-cli
  ```

---

## Installation

Add the NuGet package to your project:

```bash
dotnet add package Spectral.Net.Analyzer
```

Or add it manually to your `.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="Spectral.Net.Analyzer" Version="x.y.z">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  </PackageReference>
</ItemGroup>
```

---

## Usage

### 1. Add your API spec files as `AdditionalFiles`

```xml
<ItemGroup>
  <!-- Add every YAML/JSON file you want Spectral to lint -->
  <AdditionalFiles Include="openapi.yaml" />
  <!-- or use a glob: -->
  <AdditionalFiles Include="**/*.yaml" Exclude="bin/**;obj/**" />
</ItemGroup>
```

### 2. Build

```bash
dotnet build
```

Build output:

```
Build started...
openapi.yaml(12,5): error SPECTRAL001: [operation-operationId-unique] Every operation must have unique "operationId". [MyProject.csproj]
openapi.yaml(34,3): warning SPECTRAL002: [oas3-api-servers] OpenAPI "servers" must be present and non-empty array. [MyProject.csproj]
Build FAILED.
```

---

## Configuration

All properties are optional with sensible defaults.

| MSBuild Property | Default | Description |
|---|---|---|
| `SpectralPath` | `spectral` | Path or name of the Spectral CLI executable. |
| `SpectralRulesetFile` | *(auto-detected)* | Path or URL of the ruleset file. Auto-detects `.spectral.yaml`, `.spectral.yml`, `.spectral.json`, `.spectral.js` in the project directory. |
| `SpectralEnabled` | `true` | Set to `false` to disable Spectral analysis entirely. |

### Example

```xml
<PropertyGroup>
  <!-- Use an explicit path if spectral is not on PATH -->
  <SpectralPath>C:\tools\spectral.cmd</SpectralPath>
  <!-- Point to a shared ruleset -->
  <SpectralRulesetFile>$(SolutionDir).spectral.yaml</SpectralRulesetFile>
  <!-- Disable in Release builds -->
  <SpectralEnabled Condition="'$(Configuration)' == 'Release'">false</SpectralEnabled>
</PropertyGroup>
```

---

## Diagnostics

| ID | Default Severity | Description |
|---|---|---|
| `SPECTRAL000` | Warning | Analyzer internal error (e.g. Spectral CLI not found). |
| `SPECTRAL001` | Error | Spectral severity 0 – Error. |
| `SPECTRAL002` | Warning | Spectral severity 1 – Warning. |
| `SPECTRAL003` | Info | Spectral severity 2 – Info / Suggestion. |
| `SPECTRAL004` | Info | Spectral severity 3 – Hint. |

Severity can be overridden per-project via `.editorconfig`:

```ini
[*.yaml]
dotnet_diagnostic.SPECTRAL002.severity = error   # treat warnings as errors
dotnet_diagnostic.SPECTRAL003.severity = none     # silence info messages
```

---

## Contributing

Contributions are welcome! Please open an issue or pull request.

### Building locally

```bash
dotnet build Spectral.Net.Analyzer.slnx
dotnet test  Spectral.Net.Analyzer.slnx
dotnet pack  src/Spectral.Net.Analyzer/Spectral.Net.Analyzer.csproj
```

### Releasing

Push a `v*` tag (e.g. `v1.2.0`) to trigger the [Release workflow](.github/workflows/release.yml),
which builds, tests, packs, creates a GitHub Release, and publishes to NuGet.org.

```bash
git tag v1.0.0
git push origin v1.0.0
```

---

## License

[MIT](LICENSE) © Sander ten Brinke
