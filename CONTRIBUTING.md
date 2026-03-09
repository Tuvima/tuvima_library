# Contributing to Tuvima Library

Thank you for your interest in contributing to Tuvima Library! This guide covers everything you need to know to get started.

## Code of Conduct

This project follows the [Contributor Covenant Code of Conduct](CODE_OF_CONDUCT.md). By participating, you agree to uphold it.

## License

Tuvima Library is licensed under the **GNU Affero General Public License v3.0 (AGPLv3)**. By contributing, you agree that your contributions will be licensed under the same terms. See the [LICENSE](LICENSE) file for details.

**Important:** Any new dependency must have an AGPLv3-compatible license (MIT, Apache 2.0, BSD, LGPL). If you're unsure, ask in the PR.

---

## How to Contribute

### 1. Fork & Clone

```bash
# Fork the repository on GitHub, then:
git clone https://github.com/YOUR-USERNAME/tuvima_library.git
cd tuvima_library
git remote add upstream https://github.com/Tuvima/tuvima_library.git
```

### 2. Create a Branch

All work happens on branches — never commit directly to `main`.

```bash
# Sync with upstream first
git fetch upstream
git checkout -b feature/your-feature-name upstream/main
```

**Branch naming conventions:**

| Prefix | Use when... |
|--------|------------|
| `feature/` | Adding new functionality |
| `fix/` | Fixing a bug |
| `docs/` | Documentation-only changes |
| `chore/` | Build, CI, or tooling changes |
| `refactor/` | Code restructuring without behaviour change |

### 3. Make Your Changes

```bash
# Build — must produce 0 errors, 0 warnings
dotnet build --warnaserror

# Run tests — all must pass
dotnet test

# (Optional) Docker build validation
docker build -t tuvima-test .
```

### 4. Commit

Follow [Conventional Commits](https://www.conventionalcommits.org/):

```
feat: add MusicBrainz provider support
fix: prevent duplicate Hub creation on re-ingestion
docs: update configuration guide for Docker volumes
chore: upgrade MudBlazor to 9.1.0
```

Keep commits focused — one logical change per commit.

### 5. Push & Open a PR

```bash
git push origin feature/your-feature-name
```

Then open a Pull Request against `Tuvima/tuvima_library:main`. The PR template will guide you through the checklist.

---

## What Happens After You Open a PR

1. **CI runs automatically** — build, tests, and Docker image validation
2. **Code review** — the maintainer ([@shyfaruqi](https://github.com/shyfaruqi)) reviews all PRs
3. **Feedback** — you may be asked to make changes; push new commits to the same branch
4. **Merge** — only the maintainer can merge to `main`

### PR Requirements

All of these must pass before merge:

- [ ] CI build succeeds (0 errors, 0 warnings)
- [ ] All tests pass
- [ ] Docker build succeeds
- [ ] At least one approving review
- [ ] No unresolved review conversations

---

## Development Setup

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker](https://www.docker.com/get-started) (for container builds)
- Any code editor (VS Code, Rider, Visual Studio)

### First-Time Setup

```bash
# Copy example config to live config
cp -r config.example config

# Restore and build
dotnet restore
dotnet build

# Run the Engine
cd src/MediaEngine.Api
dotnet run

# In a separate terminal, run the Dashboard
cd src/MediaEngine.Web
dotnet run
```

The Engine listens on `http://localhost:61495` and the Dashboard on `http://localhost:5016`.

### Docker

A single container runs both the Engine and Dashboard:

```bash
# Build and run
docker compose up -d

# Engine:    http://localhost:8080
# Dashboard: http://localhost:8081

# View logs
docker compose logs -f

# Tear down
docker compose down
```

Or without Compose:

```bash
docker build -t tuvima/library .
docker run -p 8080:8080 -p 8081:8081 \
  -v tuvima-data:/data \
  -v /path/to/media:/library \
  -v /path/to/inbox:/watch \
  tuvima/library
```

---

## Project Architecture

Tuvima Library is split into two main applications:

- **Engine** (`MediaEngine.Api`) — the intelligence and data layer. REST API + SignalR.
- **Dashboard** (`MediaEngine.Web`) — the browser UI. Blazor Server, talks to the Engine via HTTP.

The codebase follows a layered architecture:

```
src/
  MediaEngine.Domain       Core business rules (no external dependencies)
  MediaEngine.Storage      SQLite persistence
  MediaEngine.Intelligence Weighted Voter scoring engine
  MediaEngine.Processors   File format readers (EPUB, video, comics, audio)
  MediaEngine.Providers    External metadata adapters (Apple Books, Wikidata, etc.)
  MediaEngine.Ingestion    Watch Folder, file processing pipeline
  MediaEngine.Identity     Authentication & authorization
  MediaEngine.Api          REST API + SignalR hub (Engine entry point)
  MediaEngine.Web          Blazor Dashboard (UI entry point)
```

See `CLAUDE.md` for the full architectural reference.

---

## Code Style

- **Target:** .NET 10, C# latest, nullable enabled
- **Formatting:** Follow the `.editorconfig` in the repo root
- **Warnings:** Treat as errors — `dotnet build --warnaserror` must pass
- **Tests:** xUnit. Put tests in `tests/MediaEngine.{Module}.Tests/`
- **No secrets in code:** API keys, paths, and credentials go in `config/` (gitignored)

---

## Reporting Issues

Use the [issue templates](https://github.com/Tuvima/tuvima_library/issues/new/choose) for bug reports and feature requests. Include reproduction steps and environment details for bugs.

---

## Questions?

Open a [Discussion](https://github.com/Tuvima/tuvima_library/discussions) for questions, ideas, or general conversation about the project.
