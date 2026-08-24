---
title: "Getting Started"
summary: "Install Tuvima Library, launch the local Engine and Dashboard, and configure the first library paths."
audience: "user"
category: "tutorial"
product_area: "library configuration"
tags:
  - "install"
  - "onboarding"
  - "first-run"
---

# Getting Started

This tutorial gets Tuvima Library running locally. By the end, the Engine and
Dashboard will be ready for catalogued intake and a profile-owned View Personal
Space.

**Time required:** 15-30 minutes, plus optional model download time for Local AI.

## Before You Begin

You need:

- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download)
- A local copy of the repository
- About 10 GB free disk space if you plan to use Local AI models
- Optional provider credentials for services that require keys, such as TMDB, Comic Vine, Fanart.tv, or OpenSubtitles

Confirm the SDK:

```bash
dotnet --version
```

The required SDK version is also listed in `global.json`.

## Step 1 - Get The Code

```bash
git clone https://github.com/Tuvima/tuvima_library.git
cd tuvima_library
dotnet restore MediaEngine.slnx
```

This repository uses normal .NET restore. It does not use npm or yarn for application startup.

## Step 2 - Review Configuration

Configuration lives under `config/`. The most important first-run files are:

- `config/core.json` - data root, database path, server name, language, and library root defaults
- `config/libraries.json` - catalogued libraries, their governed sources, the single View storage root, and shared incoming locations
- `config/providers/*.json` - provider configuration
- `config/secrets/` - provider credentials; this folder is ignored by git
- `config/ai.json` - Local AI models, feature flags, vocabulary, and schedules

If you have provider keys, place them under `config/secrets/` rather than committing them to normal config files.

## Step 3 - Start The Engine

Open a terminal from the repository root:

```bash
dotnet run --project src/MediaEngine.Api
```

Wait until you see:

```text
Now listening on: http://localhost:61495
```

Leave this terminal open. The Engine owns ingestion, storage, provider calls, Local AI, background jobs, and the HTTP/SignalR APIs.

## Step 4 - Start The Dashboard

Open a second terminal from the repository root:

```bash
dotnet run --project src/MediaEngine.Web
```

Wait until you see:

```text
Now listening on: http://localhost:5016
```

Open:

```text
http://localhost:5016
```

If your Engine runs on a different URL, set `TUVIMA_ENGINE_URL` before starting the Dashboard.

## Step 5 - Configure Sources And Begin Intake

Open **Settings > Libraries**.

Confirm or create the catalogued libraries you need. Use `catalogued /
enriched` for known books, movies, TV, music, audiobooks, and comics. These
lanes may use the administrator scan action for an existing batch.

For photos, short videos, documents, audio notes, home movies, and other
private files, configure the one View storage root under **Settings > Libraries**,
then enable View for the profile. Tuvima provisions that profile's Personal
Space automatically. Under **Settings > Users**, use **Import folder** to copy
an existing export into managed profile storage, or use **Link existing folder**
for an advanced read-only index of files that must remain where they are.
Multiple sources and future devices feed the same Personal Space and never
become separate browsing destinations.

For catalogued media, start an administrator scan when importing an existing
folder, then use **Settings > Ingestion** to watch progress. View resolves the
active profile and its Personal Space; normal Photos browsing does not expose a
source picker or routine scan action. View reconciliation is an
administrator recovery/diagnostic tool, not routine personal-media navigation.

Open **Settings > Providers** if catalogue-provider credentials need attention.
View personal media does not use those providers.

## Docker Alternative

Docker support exists for local/containerized runs:

```bash
docker compose up
```

Edit the host volume paths in `docker-compose.yml` before starting. Mount each configured source and incoming location at the path recorded in `libraries.json`; `/config`, `/db`, and `/models` persist independently. The Dashboard is exposed at `http://localhost:5016` and the Engine at `http://localhost:61495` by default.

## Stopping Tuvima

Press `Ctrl+C` in each terminal. Library data is stored automatically in SQLite; there is no manual save step.

## Next Steps

- [Your First Library](first-library.md)
- [Configure Providers](../guides/configuring-providers.md)
- [Troubleshooting](../guides/troubleshooting.md)
- [Product Status](../product/status.md)
