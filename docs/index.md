# Tuvima Library Documentation

Tuvima Library is a unified media intelligence platform that runs entirely on your own machine. It organises books, audiobooks, movies, TV shows, music, comics, and podcasts into a single coherent library — with no cloud account, no subscription, and no data leaving your home. Point it at your hard drive and it takes care of the rest.

---

## For Users

You want to install Tuvima Library, add your media, and get the most out of it.

### Tutorials — Learn by doing

Step-by-step walkthroughs for people who are new to Tuvima Library.

| Guide | Description |
|---|---|
| [Getting Started](tutorials/getting-started.md) | Install and run Tuvima Library for the first time |
| [Your First Library](tutorials/first-library.md) | Add a watch folder and see your media appear |

### How-to Guides — Get specific things done

Task-focused instructions for common goals. You already know the basics; you want to accomplish something specific.

| Guide | Description |
|---|---|
| [Adding Media](guides/adding-media.md) | Watch folders, import mode, and supported formats |
| [Resolving Reviews](guides/resolving-reviews.md) | Fix items that need your attention in the Vault |
| [Configuring Providers](guides/configuring-providers.md) | Set up API keys and provider priorities |
| [Language Setup](guides/language-setup.md) | Configure languages and CJK support |

### Reference — Look something up

Factual descriptions of every setting, status indicator, and configuration option.

| Reference | Description |
|---|---|
| [Configuration](reference/configuration.md) | Every config file and field explained |
| [Media Types](reference/media-types.md) | Supported formats, processors, and providers |
| [Glossary](reference/glossary.md) | Definitions for every term used in the project |

### Explanation — Understand how it works

Background reading that explains why Tuvima Library behaves the way it does.

| Article | Description |
|---|---|
| [How Ingestion Works](explanation/how-ingestion-works.md) | The journey from file to library entry |
| [How Scoring Works](explanation/how-scoring-works.md) | How metadata conflicts are resolved |
| [How Universes Work](explanation/how-universes-work.md) | The grouping model that links stories across formats |
| [How the AI Works](explanation/how-ai-works.md) | Local AI models, hardware tiers, and what they do |
| [How Enrichment Works](explanation/how-hydration-works.md) | Two-stage enrichment from retail providers and Wikidata |
| [How the Vault Works](explanation/how-the-vault-works.md) | The command centre for managing your library |

---

## For Developers

You want to understand the codebase, contribute code, or build something on top of Tuvima Library.

### Tutorials — Set up your environment

| Guide | Description |
|---|---|
| [Developer Setup](tutorials/dev-setup.md) | Clone, build, run, and explore the codebase |

### How-to Guides — Common development tasks

| Guide | Description |
|---|---|
| [Adding a Provider](guides/adding-a-provider.md) | Create a new metadata provider with zero code |
| [Writing a Processor](guides/writing-a-processor.md) | Add support for a new file format |
| [Running Tests](guides/running-tests.md) | Build, test, and verify changes |

### Reference — Technical specifications

| Reference | Description |
|---|---|
| [API Endpoints](reference/api-endpoints.md) | Every Engine HTTP route and SignalR hub |
| [Database Schema](reference/database-schema.md) | All tables, columns, and relationships |
| [Configuration](reference/configuration.md) | Every config file and field explained |
| [Glossary](reference/glossary.md) | Definitions for every term used in the project |

### Explanation — Architecture and design decisions

| Article | Description |
|---|---|
| [All Explanation articles](explanation/) | Ingestion, scoring, universes, AI, enrichment, and the Vault |

### Architecture deep-dives

Detailed technical documentation for each subsystem lives in the [`architecture/`](architecture/) directory.

| Document | Covers |
|---|---|
| [architecture/ingestion-pipeline.md](architecture/ingestion-pipeline.md) | How files move from disk into the library |
| [architecture/scoring-and-cascade.md](architecture/scoring-and-cascade.md) | Priority Cascade and metadata conflict resolution |
| [architecture/hydration-and-providers.md](architecture/hydration-and-providers.md) | Two-stage enrichment: retail providers and Wikidata |
| [architecture/dashboard-ui.md](architecture/dashboard-ui.md) | Dashboard design system and component layout |
| [architecture/settings-and-vault.md](architecture/settings-and-vault.md) | Settings screens, Library Vault, and Vault page design |
| [architecture/universe-graph.md](architecture/universe-graph.md) | Universe graph, Chronicle Engine, and SPARQL queries |
| [architecture/ai-integration.md](architecture/ai-integration.md) | Local AI: models, features, and hardware tiers |
| [architecture/security.md](architecture/security.md) | Authentication, API keys, roles, and rate limiting |
| [architecture/target-state.md](architecture/target-state.md) | Planned features not yet implemented |

---

## Quick Links

- [Getting Started](tutorials/getting-started.md) — First-time install
- [Your First Library](tutorials/first-library.md) — Add media and watch it appear
- [Developer Setup](tutorials/dev-setup.md) — Build and run from source
- [Configuration Reference](reference/) — Every config file explained
- [Architecture Overview](architecture/) — Technical deep-dives
