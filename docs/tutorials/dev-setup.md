---
title: "Developer Setup"
summary: "Clone the repo, configure local development, and run the services needed to work on Tuvima Library."
audience: "developer"
category: "tutorial"
product_area: "developer"
tags:
  - "contributors"
  - "setup"
  - "local-dev"
---

# Developer Setup

This tutorial walks you through cloning the repository, building the solution, running the test suite, and getting the Engine and Dashboard running locally. By the end you will have a working development environment and an understanding of where things live in the codebase.

---

## Prerequisites

- **.NET 10 SDK** â€” `dotnet --version` should report `10.0.x` or later. Download from [dot.net](https://dotnet.microsoft.com/en-us/download).
- **Git** â€” `git --version` to confirm.
- Approximately **10 GB free disk space** (AI models download on first Engine startup).

No other global tools are required. All project dependencies are declared in `.csproj` files and restored by the .NET toolchain.

---

## Step 1 â€” Clone and branch

```bash
git clone https://github.com/shyfaruqi/tuvima-library.git
cd tuvima-library
git checkout -b feature/your-branch-name
```

The `main` branch is the integration target. All work goes on a feature branch.

---

## Step 2 â€” Project structure overview

The solution is split into focused projects under `src/` and `tests/`. Each project has a single responsibility:

| Project | Role |
|---|---|
| `src/MediaEngine.Domain` | Domain entities, interfaces, value objects. Pure business logic â€” no I/O dependencies. |
| `src/MediaEngine.Storage` | SQLite data access via Dapper. Repositories, migrations, and query logic. |
| `src/MediaEngine.Intelligence` | Priority Cascade engine. Scores and resolves metadata claims. |
| `src/MediaEngine.Processors` | File processors â€” reads embedded metadata from EPUB, ID3 tags, video containers, etc. |
| `src/MediaEngine.Providers` | External provider adapters â€” Apple API, Google Books, TMDB, Wikidata Reconciliation API. |
| `src/MediaEngine.Ingestion` | Folder watcher, ingestion pipeline, file organiser, staging logic. |
| `src/MediaEngine.AI` | Local LLM and Whisper inference. Hardware profiling, model management, AI feature implementations. |
| `src/MediaEngine.Api` | ASP.NET Core host. HTTP endpoints, SignalR hub, background services. Exposes the Engine. |
| `src/MediaEngine.Web` | Blazor Server host. Dashboard UI â€” components, pages, services. |
| `tests/` | xUnit test projects, one per domain area. |

The `config/` directory holds all runtime configuration as individual JSON files (gitignored â€” copy from `config.example/`). The `.data/` directory holds the SQLite database, cover art images, and staging files (gitignored).

---

## Step 3 â€” Configuration

Copy the example configuration:

```bash
cp -r config.example config
```

The key file is `config/core.json`. The defaults work without modification for local development:

```json
{
  "database_path": ".data/database/library.db",
  "data_root": ".data"
}
```

Other config files of interest during development:

| File | Purpose |
|---|---|
| `config/ai.json` | AI model paths, hardware tier overrides, feature flags |
| `config/libraries.json` | Watch folders (add entries here to seed a dev library) |
| `config/scoring.json` | Priority Cascade weights and tier configuration |
| `config/providers/*.json` | Per-provider settings (endpoints, API keys, language strategy) |
| `config/hydration.json` | Hydration pipeline slot configuration |

Sensitive values (API keys for external providers) go in `config/providers/*.json`. These files are gitignored â€” never commit them.

---

## Step 4 â€” Build

From the repository root:

```bash
dotnet build
```

The build must produce **0 errors and 0 warnings**. If warnings appear, treat them as errors â€” the CI pipeline enforces this. Investigate and fix before proceeding.

If you see `NU1101` package restore errors, check your NuGet source configuration. The `Tuvima.WikidataReconciliation` package is published to the project's private NuGet feed; ensure your `NuGet.Config` points to it.

---

## Step 5 â€” Run tests

```bash
dotnet test
```

All tests must pass before committing. Test projects mirror the `src/` structure:

```
tests/
  MediaEngine.Domain.Tests/
  MediaEngine.Storage.Tests/
  MediaEngine.Intelligence.Tests/
  MediaEngine.Processors.Tests/
  MediaEngine.Providers.Tests/
  MediaEngine.Ingestion.Tests/
```

To run a specific project:

```bash
dotnet test tests/MediaEngine.Intelligence.Tests/
```

To run with coverage:

```bash
dotnet test --collect:"XPlat Code Coverage"
```

Coverage reports are written to `TestResults/` inside each test project.

---

## Step 6 â€” Start the Engine

```bash
dotnet run --project src/MediaEngine.Api
```

The Engine starts on `http://localhost:61495`. On first run it will:

1. Run a hardware benchmark (10â€“30 seconds).
2. Apply any pending SQLite migrations automatically.
3. Start the folder watcher for any configured library folders.
4. Begin downloading AI models in the background (~9 GB total).

The Engine is ready when you see:

```
Now listening on: http://localhost:61495
```

To run without AI model downloads (useful for UI-only development):

```json
// config/ai.json
{
  "enabled": false
}
```

---

## Step 7 â€” Start the Dashboard

Open a second terminal:

```bash
dotnet run --project src/MediaEngine.Web
```

The Dashboard starts on `http://localhost:5016`. Open it in your browser.

Hot reload is available during development:

```bash
dotnet watch --project src/MediaEngine.Web
```

This rebuilds and refreshes the browser on file saves. Note: Blazor Server hot reload works best for component changes. Changes to DI registrations, middleware, or static assets may require a full restart.

---

## Step 8 â€” Swagger / interactive API explorer

With the Engine running, open:

```
http://localhost:61495/swagger
```

This shows all Engine endpoints, organised by controller. You can send requests directly from the browser â€” useful for testing endpoint behaviour without writing test code.

The Swagger UI is generated automatically from XML doc comments on controller methods. Keep doc comments up to date when adding or modifying endpoints.

---

## Step 9 â€” Key conventions

### Headless design: Engine and Dashboard are strictly separated

The Dashboard never imports types from Engine projects. All data flows via HTTP (REST) and SignalR. The contract lives in:

- `src/MediaEngine.Web/Services/Integration/ILibraryApiClient.cs` â€” interface for all Engine calls
- `src/MediaEngine.Web/Services/Integration/LibraryApiClient.cs` â€” implementation (maps JSON responses to view DTOs)
- `src/MediaEngine.Web/Models/ViewDTOs/` â€” data shapes used only by the Dashboard

Never add a project reference from `MediaEngine.Web` to any Engine project.

### Feature-Sliced Dashboard layout

Dashboard code is organised by feature slice, not by technical layer. When adding new UI code, put it in the correct slice:

| Code type | Location |
|---|---|
| Engine HTTP call | `Services/Integration/LibraryApiClient.cs` + interface |
| Dashboard data shape | `Models/ViewDTOs/` |
| Reusable component | `Components/<FeatureName>/` |
| Full page (routed) | `Components/Pages/` |
| Vault sub-component | `Components/Vault/` |
| Settings tab | `Components/Settings/` |

See `CLAUDE.md` Â§6 for the full layout reference.

### SQLite with Dapper

The data access layer uses Dapper (not Entity Framework). SQL queries are written by hand in repository classes under `src/MediaEngine.Storage/Repositories/`. Migrations are numbered sequentially (`M-001`, `M-002`, â€¦) and applied automatically at startup by `MigrationRunner`.

When adding a new column or table:
1. Create a new migration file in `src/MediaEngine.Storage/Migrations/`.
2. Number it one above the current highest migration.
3. Register it in `MigrationRunner.cs`.
4. Never modify an existing migration that has been applied to production.

### Zero warnings policy

The solution sets `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in `Directory.Build.props`. New code must be warning-free. This includes nullable reference type warnings â€” all new code must be null-safe.

---

## Step 10 â€” Where to find things

**Engine endpoints** live in `src/MediaEngine.Api/Endpoints/`. Each file groups related actions (e.g., `LibraryEndpoints.cs`, `VaultEndpoints.cs`, `AiEndpoints.cs`). Endpoints use minimal API style (`MapGet`, `MapPost`, etc.) registered in `Program.cs`.

**Domain entities** live in `src/MediaEngine.Domain/Entities/`. The core hierarchy: `MediaAsset` â†’ `Edition` â†’ `Work` â†’ `Hub` (Series) â†’ `ParentHub` (Universe).

**Dashboard components** live in `src/MediaEngine.Web/Components/`. Navigation is in `Components/Navigation/`, Vault in `Components/Vault/`, Settings in `Components/Settings/`.

**Background services** live in `src/MediaEngine.Api/Services/`. Long-running services (ingestion watcher, hydration scheduler, AI batch processor) are registered as `IHostedService` implementations.

**Provider adapters** live in `src/MediaEngine.Providers/Adapters/`. Each adapter implements `IMetadataProvider`. The `ReconciliationAdapter` handles all Wikidata interaction.

**AI features** live in `src/MediaEngine.AI/Features/`. Each feature is a focused service (e.g., `SmartLabeler`, `VibeTagger`, `QidDisambiguator`, `DescriptionIntelligenceService`).

---

## Commit conventions

Commits follow this format:

```
Short summary line (imperative mood, 72 chars max)

Optional longer body explaining why, not what.

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
```

The build must pass before committing. Run `dotnet build` and `dotnet test` and confirm 0 errors before pushing.

---

## Next steps

- Read the [architecture deep-dives](../architecture/ingestion-pipeline.md) for the subsystem you are working on.
- Check `CLAUDE.md` for project conventions and vocabulary rules.
- Check `MEMORY.md` for recorded architectural decisions.
- Use `http://localhost:61495/swagger` to explore the Engine's action surface while reading endpoint code.

## Related

- [How to Build, Test, and Verify Changes](../guides/running-tests.md)
- [How to Write a New File Format Processor](../guides/writing-a-processor.md)
- [How to Add a New Metadata Provider](../guides/adding-a-provider.md)
