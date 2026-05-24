---
title: "Getting Started"
summary: "Install Tuvima Library, launch the local Engine and Dashboard, and run the first setup checks."
audience: "user"
category: "tutorial"
product_area: "setup"
tags:
  - "install"
  - "onboarding"
  - "first-run"
---

# Getting Started

This tutorial gets Tuvima Library running locally. By the end, the Engine and Dashboard will be ready for your first library scan.

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
- `config/libraries.json` - watched/imported library folders
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

## Step 5 - Run The Setup Checklist

In the Dashboard, open **Settings > Setup**.

The checklist verifies:

- Engine connection
- folder readiness
- provider readiness
- optional Local AI status
- scan state
- pending Review Queue work

This is the current first-run path. A richer guided wizard is still outstanding, so the checklist is intentionally honest about partial or unavailable areas.

## Step 6 - Configure Folders And Scan

Open **Settings > Libraries**.

Confirm:

- Watch Folder
- Library Root
- organization template
- path read/write checks

Save changes, then run **Scan Now** from Setup or **Scan saved watch folder** from Libraries. Open **Settings > Ingestion** to watch progress.

## Docker Alternative

Docker support exists for local/containerized runs:

```bash
docker compose up
```

Edit `docker-compose.yml` before starting if you need different config, data, or media paths. The Dashboard is exposed at `http://localhost:5016` and the Engine at `http://localhost:61495` by default.

## Stopping Tuvima

Press `Ctrl+C` in each terminal. Library data is stored automatically in SQLite; there is no manual save step.

## Next Steps

- [Your First Library](first-library.md)
- [Configure Providers](../guides/configuring-providers.md)
- [Troubleshooting](../guides/troubleshooting.md)
- [Product Status](../product/status.md)
