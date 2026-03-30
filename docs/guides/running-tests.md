---
title: "How to Build, Test, and Verify Changes"
summary: "Run the project quality checks and verify changes before you commit or open a pull request."
audience: "developer"
category: "guide"
product_area: "testing"
tags:
  - "build"
  - "test"
  - "verification"
---

# How to Build, Test, and Verify Changes

This guide covers the full verification workflow: building the solution, running unit
tests, using the development endpoints for integration testing, and cleaning up.

---

## Prerequisites

- .NET 10 SDK installed (`dotnet --version` should return `10.x.x`)
- The solution root is `tuvima-library/`
- Both projects built successfully at least once so restore has run

---

## 1. Building the solution

Always build from the repository root. The build must produce **0 errors and 0 warnings**
before any change is considered complete.

```bash
dotnet build
```

A clean build looks like:

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

Any warning is treated as a defect and must be resolved. Common warning sources:

- Nullable reference annotations: add `?` or null-check as appropriate
- Unused `using` directives: remove them
- Async method without `await`: either add `await` or return a `ValueTask`/`Task.CompletedTask`

### Building a single project

When iterating on one project to get faster feedback:

```bash
dotnet build src/MediaEngine.Processors/MediaEngine.Processors.csproj
dotnet build src/MediaEngine.Api/MediaEngine.Api.csproj
```

---

## 2. Running the unit test suite

```bash
dotnet test
```

This runs all test projects under `tests/`. Each project maps to one source project:

| Test project | Tests for |
|---|---|
| `MediaEngine.Domain.Tests` | Domain entities, enums, business rules |
| `MediaEngine.Storage.Tests` | Repository queries, migration correctness |
| `MediaEngine.Intelligence.Tests` | Priority Cascade scoring logic |
| `MediaEngine.Processors.Tests` | `IMediaProcessor` implementations, `MediaProcessorRegistry` |
| `MediaEngine.Providers.Tests` | Provider config parsing, field mapping logic |
| `MediaEngine.Ingestion.Tests` | Ingestion pipeline steps |
| `MediaEngine.AI.Tests` | AI feature contracts, hardware tier policy |
| `MediaEngine.Api.Tests` | API key generation, path validation |

To run a single project:

```bash
dotnet test tests/MediaEngine.Processors.Tests
```

To run with verbose output (shows individual test names):

```bash
dotnet test --logger "console;verbosity=normal"
```

To run with code coverage (requires coverlet, already referenced in test projects):

```bash
dotnet test --collect:"XPlat Code Coverage"
```

Coverage reports land in `tests/*/TestResults/`.

---

## 3. Starting the Engine for manual testing

The Engine (`MediaEngine.Api`) runs on `http://localhost:61495`.

```bash
dotnet run --project src/MediaEngine.Api/MediaEngine.Api.csproj
```

The Dashboard (`MediaEngine.Web`) runs on `http://localhost:5016`.

```bash
dotnet run --project src/MediaEngine.Web/MediaEngine.Web.csproj
```

Run both in separate terminals. The Dashboard makes HTTP requests to the Engine;
both must be running for the full UI to work.

---

## 4. Swagger â€” interactive API exploration

With the Engine running, open:

```
http://localhost:61495/swagger
```

Swagger lists every endpoint grouped by tag. Use it to:

- Explore what routes exist and their request/response schemas
- Test individual endpoints with real data without writing code
- Inspect authentication requirements (most endpoints require an `Authorization: ApiKey <key>` header)

The local development environment has an authentication bypass for `localhost` traffic
(configurable in `config/server.json`), so you can call most endpoints from Swagger
without setting an API key during development.

---

## 5. Development-only endpoints

The following endpoints are **only registered when `ASPNETCORE_ENVIRONMENT == "Development"`**.
They are absent in any other environment.

### POST /dev/seed-library

Drops synthetic test files (EPUB, MP3, MP4, FLAC, CBZ) into the configured Watch Folders.
The ingestion engine picks them up automatically if Watch mode is active.

```bash
curl -X POST http://localhost:61495/dev/seed-library
```

Returns a summary of how many files were seeded per media type.

### POST /dev/wipe

Wipes the database, library root, and Watch Folder, then re-initialises the empty database.
Stops the ingestion engine first to avoid processing files mid-wipe.

```bash
curl -X POST http://localhost:61495/dev/wipe
```

Use this before a full integration test run to start from a clean state.

### POST /dev/full-test

Runs wipe then seed in sequence and returns a per-media-type summary.

```bash
curl -X POST http://localhost:61495/dev/full-test
```

### POST /dev/integration-test

The most thorough validation. Runs the complete cycle:

1. Wipes database and library root
2. Seeds synthetic files for all media types
3. Waits for the ingestion engine to process them
4. Validates identification rates, metadata quality, and pipeline completion
5. Writes a timestamped HTML report to `tools/reports/` and `src/MediaEngine.Api/DevSupport/`

```bash
curl -X POST http://localhost:61495/dev/integration-test
```

The report file is named `integration-test-YYYY-MM-DD-HHmmss.html`. Open it in a browser
to see per-asset results, claim values, confidence scores, and any failures. The latest
run is always aliased to `tools/reports/integration-test-latest.html`.

---

## 6. Debug lookup â€” test enrichment without persisting

The `/debug/lookup` endpoint runs a live Wikidata Reconciliation + retail provider pass
against a given title, returning every claim that would be written to the database â€”
without actually writing anything.

```http
POST http://localhost:61495/debug/lookup
Content-Type: application/json

{
  "title": "Dune",
  "author": "Frank Herbert",
  "mediaType": "Books",
  "isbn": "9780441013593"
}
```

Use this to verify that a new provider config returns claims, or to debug why an item
is not matching during hydration.

---

## 7. SignalR â€” testing real-time events

The SignalR hub for real-time dashboard updates is at:

```
ws://localhost:61495/hubs/intercom
```

Connect using the `@microsoft/signalr` client (already vendored in the Dashboard) or
a standalone tool like `wscat`:

```bash
wscat -c ws://localhost:61495/hubs/intercom
```

Events published by the Engine include ingestion progress updates, hydration stage
completions, and library change notifications. Event names and payload shapes are
defined in `src/MediaEngine.Web/Services/Integration/IntercomEvents.cs`.

To observe events during a seeded test run:
1. Connect a SignalR client to `/hubs/intercom`
2. POST to `/dev/seed-library`
3. Watch for ingestion progress and completion events in real time

---

## 8. Configuration for development

All config files live in `config/`. They are JSON files with one concern per file.
Example files (safe to commit) are in `config.example/`. Live files are gitignored.

Key files for development:

| File | Purpose |
|---|---|
| `config/core.json` | Library root, display name, language |
| `config/libraries.json` | Watch Folder paths and media type assignments |
| `config/server.json` | Port, auth bypass, rate limits |
| `config/hydration.json` | Stage 1/2 pipeline config |
| `config/providers/*.json` | One file per metadata provider |
| `config/ai.json` | AI model roles and feature toggles |
| `config/scoring.json` | Priority Cascade weights |

If a config file is missing on startup, the Engine logs a warning and uses defaults
for most settings. Required files (database path, library root) will cause a startup
failure with a descriptive error in `engine.log`.

---

## 9. Serilog logs

The Engine writes rolling logs to `engine.log` in the repository root. Use it to
diagnose silent failures, HTTP errors from providers, and ingestion pipeline events.

The log level defaults to `Information`. To see all provider HTTP activity, temporarily
set `Minimum Level: Verbose` in the Serilog config section of `appsettings.Development.json`
(not committed) or `config/server.json` depending on your setup.

---

## 10. Cleaning up after work

After finishing any coding session, stop all `dotnet` processes to release file locks
on the SQLite database and log files:

```bash
taskkill //F //IM dotnet.exe
```

On Linux/macOS:

```bash
pkill -f dotnet
```

This is especially important before switching branches or running migrations, as an
open `dotnet` process holds a write lock on `library.db`.

---

## 11. Code conventions quick reference

Follow these conventions so the build stays clean and code review is predictable.

### Endpoint registration

New API endpoints go in `src/MediaEngine.Api/Endpoints/`. Each file registers a feature
group via a static extension method on `IEndpointRouteBuilder`:

```csharp
public static class MyFeatureEndpoints
{
    public static IEndpointRouteBuilder MapMyFeatureEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/my-feature").WithTags("MyFeature");

        group.MapGet("/", async (...) => { ... });
        group.MapPost("/action", async (...) => { ... });

        return app;
    }
}
```

Register the method in `Program.cs` alongside the other `MapXxxEndpoints` calls.

### Dashboard UI (Feature-Sliced layout)

New Dashboard code goes into the correct slice of `src/MediaEngine.Web/`:

| What you're adding | Where it goes |
|---|---|
| Engine HTTP call | `Services/Integration/LibraryApiClient.cs` + interface |
| Dashboard data shape | `Models/ViewDTOs/` |
| Reusable component | `Components/<FeatureName>/` |
| Full page (routed) | `Components/Pages/` |
| Vault sub-component | `Components/Vault/` |
| Settings tab | `Components/Settings/{GroupName}Tab.razor` |

### Config files

One concern per file. Names are lowercase with underscores. Provider configs go in
`config/providers/`. Never mix multiple subsystem settings into one file.

---

## 12. Common issues

**Build error: `CS8618` â€” Non-nullable property not initialised**
Add `= null!;` for properties initialised by the framework (e.g. `[Inject]` in Blazor
components), or make the property nullable with `?` if it can legitimately be null.

**Build warning: `CS1998` â€” Async method lacks `await`**
Either add `await` to an async operation in the body, or remove `async` and return
`Task.CompletedTask` or `ValueTask.CompletedTask`.

**`dotnet test` fails with SQLite locked**
Another `dotnet` process has the database open. Run `taskkill //F //IM dotnet.exe`
(Windows) or `pkill -f dotnet` (Linux/macOS) and retry.

**`/dev/seed-library` returns 0 seeded files**
The Watch Folder path in `config/libraries.json` does not exist or the Engine does
not have write permission. Check the path and create the directory if needed.

**Provider returns no claims in `/debug/lookup`**
Check: `enabled: true` in the provider config, `can_handle.media_types` includes the
requested type, and the `required_fields` for at least one search strategy are present
in the request body. See `engine.log` for HTTP-level errors.

**Dashboard shows no data after seeding**
The Dashboard connects to the Engine via SignalR and HTTP. Verify both services are
running, and that the Engine base URL in `src/MediaEngine.Web/appsettings.Development.json`
matches `http://localhost:61495`.

---

## Related

- [Developer Setup](../tutorials/dev-setup.md)
- [Engine API Reference](../reference/api-endpoints.md)
- [Database Schema Reference](../reference/database-schema.md)
