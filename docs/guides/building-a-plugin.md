---
title: "Build a Plugin"
summary: "How developers create dynamic Tuvima Library plugins and submit them for the approved catalog."
audience: "developer"
category: "guide"
product_area: "plugins"
tags:
  - "plugins"
  - "development"
  - "dotnet"
---

# Build a Plugin

Tuvima Library plugins are .NET class libraries loaded by the Engine from `{library_root}/.data/plugins`. A plugin ships as a folder containing a `plugin.json` manifest and the compiled assembly named by that manifest.

The current public extension surface lives in `src/MediaEngine.Plugins`.

## What a plugin can do today

The live contracts support:

- `ITuvimaPlugin` as the plugin entry point.
- `IPlaybackSegmentDetector` for playback markers such as commercial, intro, recap, and credits segments.
- `IPluginHealthCheck` for setup and dependency checks.
- `IPluginJob` for plugin-owned work.
- `IPluginSettingsSchemaProvider` for future richer settings UI.
- `IPluginToolRuntime` for checksum-pinned external tool resolution and execution.
- `IPluginAiClient` for declared local AI calls.

Do not depend on Web, API, Storage, Providers, Ingestion, or UI implementation types from a plugin. Keep plugin code on the plugin contracts and ordinary .NET APIs.

## Create the project

Create a class library targeting the same .NET version as Tuvima Library.

```powershell
dotnet new classlib -n Tuvima.Plugin.Sample
dotnet add Tuvima.Plugin.Sample reference path\to\tuvima-library\src\MediaEngine.Plugins\MediaEngine.Plugins.csproj
```

For plugins published outside the main repo, reference a versioned package of `MediaEngine.Plugins` when one is available. During local development, a project reference is fine.

## Implement `ITuvimaPlugin`

```csharp
using System.Text.Json;
using MediaEngine.Plugins;

namespace Tuvima.Plugin.Sample;

public sealed class SamplePlugin : ITuvimaPlugin
{
    public PluginManifest Manifest { get; } = new()
    {
        Id = "example.sample-plugin",
        Name = "Sample Plugin",
        Version = "0.1.0",
        MinimumTuvimaApiVersion = "1.0.0",
        Description = "Shows the minimum plugin shape.",
        Capabilities =
        [
            new PluginCapabilityDescriptor
            {
                Kind = "plugin-health-check",
                Name = "Sample health",
                Description = "Reports that the plugin loaded.",
            },
        ],
        Permissions = [],
        SupportedPlatforms = ["win-x64", "linux-x64", "osx-x64", "osx-arm64"],
        DefaultSettings = new Dictionary<string, JsonElement>
        {
            ["enabled_feature"] = JsonSerializer.SerializeToElement(true),
        },
    };

    public IReadOnlyList<IPluginCapability> CreateCapabilities() =>
    [
        new SampleHealthCheck(),
    ];
}
```

## Ship `plugin.json`

Dynamic plugins must include a manifest file named `plugin.json` in the plugin folder. The manifest overrides the compiled manifest metadata for display and settings defaults.

```json
{
  "id": "example.sample-plugin",
  "name": "Sample Plugin",
  "version": "0.1.0",
  "minimum_tuvima_api_version": "1.0.0",
  "description": "Shows the minimum plugin shape.",
  "entry_assembly": "Tuvima.Plugin.Sample.dll",
  "entry_type": "Tuvima.Plugin.Sample.SamplePlugin",
  "capabilities": [
    {
      "kind": "plugin-health-check",
      "name": "Sample health",
      "description": "Reports that the plugin loaded."
    }
  ],
  "permissions": [],
  "supported_platforms": ["win-x64", "linux-x64", "osx-x64", "osx-arm64"],
  "default_settings": {
    "enabled_feature": true
  }
}
```

Required dynamic plugin fields:

- `id`
- `name`
- `version`
- `entry_assembly`
- `entry_type`

Use reverse-DNS style ids when possible, such as `com.example.sample-plugin`.

## Package locally

Build the plugin, then copy the output and manifest into one folder:

```text
sample-plugin/
  plugin.json
  Tuvima.Plugin.Sample.dll
  Tuvima.Plugin.Sample.deps.json
  other-required-files.dll
```

Place the folder under:

```text
{library_root}/.data/plugins/sample-plugin/
```

Restart the Engine, open **Settings > Plugins**, and confirm the plugin loads without a setup warning.

## Use permissions honestly

Declare only what the plugin needs.

Common permissions:

- `media.read`: reads media file metadata or streams.
- `process.execute`: runs a local tool through `IPluginToolRuntime`.
- `tool.download`: allows tool auto-install when a checksum-pinned download is declared.
- `ai.infer`: calls local AI through `IPluginAiClient`.

If a plugin uses tools, declare each `tool_requirements` entry with the executable name, license, source URL, supported platforms, download URL, SHA-256, and relative executable path when auto-install is supported.

If a plugin uses local AI, declare `ai_permissions` with role, token limit, schedule, and resource class. The AI client checks these declarations before running inference.

## Submit for the approved catalog

To be listed as an approved plugin, publish a GitHub release that includes:

- A versioned archive containing `plugin.json` and compiled files.
- A SHA-256 checksum for the archive.
- Source repository URL.
- Release URL.
- Minimum Tuvima API version.
- Capability list.
- Install notes and tool requirements.
- License information for plugin code and bundled or downloaded tools.

Maintainers can then add the release to `docs/reference/approved-plugins.json`.

Plain English summary: A plugin is a small .NET package with a manifest. Developers implement the plugin contracts, package the assembly and `plugin.json` together, test it from the library `.data/plugins` folder, and publish a checksummed GitHub release for catalog approval.
