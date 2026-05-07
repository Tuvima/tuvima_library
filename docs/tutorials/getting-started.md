---
title: "Getting Started"
summary: "Install Tuvima Library, create your local configuration, and launch the Engine and Dashboard for the first time."
audience: "user"
category: "tutorial"
product_area: "setup"
tags:
  - "install"
  - "onboarding"
  - "first-run"
---

# Getting Started

This tutorial walks you through installing and running Tuvima Library for the first time. By the end, you will have the Engine and Dashboard running on your machine, ready to receive your media.

**Time required:** 15â€“30 minutes (plus download time for AI models on first startup).

---

## Before you begin

You will need:

- **.NET 10 SDK** â€” download from [dot.net](https://dotnet.microsoft.com/en-us/download). Run `dotnet --version` to confirm it is installed. You need version 10.0 or later.
- **10 GB of free disk space** â€” about 9 GB is used by the local AI models that download on first startup. The rest is for your library data.
- A copy of the Tuvima Library source code (see step 1 below).

---

## Step 1 â€” Get the code

Open a terminal and clone the repository:

```bash
git clone https://github.com/shyfaruqi/tuvima-library.git
cd tuvima-library
```

---

## Step 2 â€” Create your local configuration

The repository includes all configuration files directly in the `config/` directory. You only need to add secret files for any providers that require API keys:

```bash
# Create secret files for any providers that require API keys
# e.g. config/secrets/tmdb.json with {"api_key": "your-key"}
```

Provider secrets go in `config/secrets/` which is never committed to version control, so your keys stay private.

---

## Step 3 â€” Set your data paths

Open `config/core.json` in any text editor. You will see something like this:

```json
{
  "database_path": ".data/database/library.db",
  "data_root": ".data"
}
```

- **`database_path`** â€” where Tuvima Library stores its data store. The default (`.data/database/library.db`) keeps it inside the project folder. You can change this to any path on your machine.
- **`data_root`** â€” the root directory for all internally managed files: cover art, staging files, and generated thumbnails. The default keeps everything under `.data/` inside the project folder.

If you are happy with the defaults, you do not need to change anything. If you want your data store or images on a different drive, update these paths now.

Save the file when you are done.

---

## Step 4 â€” Start the Engine

The Engine is the intelligence layer â€” it watches your folders, processes files, and manages your library data.

Open a terminal window and run:

```bash
dotnet run --project src/MediaEngine.Api
```

The first time you start the Engine, several things happen automatically:

- **Hardware benchmark** â€” the Engine checks your CPU and RAM to decide which AI features to enable. This takes about 10â€“30 seconds.
- **AI model download** â€” the local AI models download in the background. The total size is roughly 9 GB. You will see progress messages in the terminal. The Engine is usable immediately â€” the models download while you work and activate as they become available.
- **File watcher starts** â€” once the Engine is running, it is ready to watch folders for new media.

You will see a line like this when the Engine is ready:

```
Now listening on: http://localhost:61495
```

Leave this terminal window open.

---

## Step 5 â€” Start the Dashboard

The Dashboard is the browser interface. It asks the Engine for data and displays it.

Open a second terminal window and run:

```bash
dotnet run --project src/MediaEngine.Web
```

You will see:

```
Now listening on: http://localhost:5016
```

Leave this terminal window open too.

---

## Step 6 â€” Open the Dashboard

Open your browser and go to:

```
http://localhost:5016
```

If this is your first time, the First-Run Wizard will appear. It walks you through:

1. Setting a library name
2. Choosing your AI feature preferences
3. Adding your first watch folder

You can skip the wizard and configure everything later in Settings if you prefer.

---

## What to do next

Now that Tuvima Library is running, add your media:

- [Your First Library](first-library.md) â€” Add a watch folder and see your files appear in the Dashboard.

---

## Docker alternative

If you prefer to run Tuvima Library in a container rather than installing .NET directly, a `docker-compose.yml` is provided at the root of the repository.

```bash
docker compose up
```

This starts both the Engine and Dashboard in containers. The Dashboard is accessible at `http://localhost:5016` and the Engine at `http://localhost:61495`. Configuration and data directories are mounted from your machine as volumes â€” edit `docker-compose.yml` to point them at the right paths before starting.

Note: AI model downloads still happen on first startup and require internet access from inside the container. Subsequent starts are instant once models are cached.

---

## Stopping Tuvima Library

Press `Ctrl+C` in each terminal window to stop the Engine and Dashboard. Your library data is saved to the data store automatically â€” there is nothing to manually save.

## Related

- [Your First Library](first-library.md)
- [How to Configure Metadata Providers](../guides/configuring-providers.md)
- [Configuration Reference](../reference/configuration.md)

