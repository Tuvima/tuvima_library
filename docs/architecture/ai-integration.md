# Local AI Intelligence Layer

## Role in the System

AI is a core function of Tuvima Library, not an optional add-on. It replaces brittle heuristic and regex code that previously handled filename cleaning, media type disambiguation, and metadata scoring. The Engine requires AI models to be present and will not begin ingestion until they have been downloaded.

All inference runs entirely on the local machine. No cloud account, no subscription, no data leaves the NAS.

---

## Project Structure

All AI implementations live in `MediaEngine.AI`. This project sits alongside `MediaEngine.Providers` in the dependency chain — it references `MediaEngine.Domain` (for contracts) and `MediaEngine.Storage` (for configuration).

NuGet dependencies:
- `LLamaSharp` + `LLamaSharp.Backend.Cpu` (both MIT) — .NET native llama.cpp binding with GBNF grammar constraint support
- `Whisper.net` + `Whisper.net.Runtime` (both MIT) — .NET native whisper.cpp binding for speech-to-text and language detection

---

## Model Roles

Three model roles cover all AI workloads. Only one model is loaded into memory at a time, enforced by a `SemaphoreSlim`. Models auto-unload after a configurable idle timeout. On first run, `ModelAutoDownloadService` downloads all models to the `/models` Docker volume (environment variable: `TUVIMA_MODELS_DIR`).

| Role | Default model | Memory footprint | Use |
|---|---|---|---|
| `text_fast` | Llama 3.2 1B Q4_K_M | ~750 MB | On-demand tasks: search intent parsing, TL;DR, recommendation explanations |
| `text_quality` | Llama 3.2 3B Q4_K_M | ~2 GB | Batch tasks: ingestion manifest analysis, vibe tagging, QID disambiguation |
| `audio` | Whisper Medium | ~1.5 GB | Audio tasks: transcription, language detection, sync maps |

Model roles are configurable in `config/ai.json`. Any GGUF-format model can be substituted by updating the config.

---

## Structured Output

All LLM calls use GBNF grammar constraints — llama.cpp forces the model to produce valid JSON at the token level. This is model-agnostic and works with Llama, Mistral, Phi, Gemma, and Qwen models. JSON schema validation and retry logic serve as a safety net over the grammar constraint.

---

## Features

### Ingestion (automatic, runs during file processing)

| Feature | Model | What it does |
|---|---|---|
| Smart Labeling | text_quality | Cleans raw filenames into structured title/author/year/series fields. Replaces `TitleNormalizer.cs`. |
| Media Type Classification | text_quality | Classifies ambiguous file formats (MP3, MP4, M4A) when heuristic signals are insufficient. Replaces AudioProcessor/VideoProcessor disambiguation heuristics. |
| Batch Manifest Builder | text_quality | Analyses an entire folder of files as a group before retail API calls, inferring series, author, and format context. Reduces retail API calls by 80–95% for bulk imports. Supersedes `IngestionHintCache`. |
| Audio Language Detection | audio | Detects the spoken language of audio files using Whisper. |

### Alignment (automatic / on-demand)

| Feature | Model | What it does |
|---|---|---|
| QID Disambiguation | text_quality | When the Reconciliation API returns multiple Wikidata candidates, picks the best match using semantic reasoning over title, description, and existing metadata. |
| Series Alignment | text_quality | Infers correct reading/watching order within a series when Wikidata series position data is absent or inconsistent. Background service (3 AM daily). |
| Watching Order | text_fast | Generates a recommended cross-media consumption order for a Hub (read the book before watching the film, etc.). On-demand. |

### Enrichment (background / on-demand)

| Feature | Model | What it does |
|---|---|---|
| Vibe Tags | text_quality | Generates mood and atmosphere tags using per-category vocabularies defined in `config/ai.json`. Background service (4 AM daily). |
| TL;DR | text_fast | Generates a 2–3 sentence plain-language summary of a work's description. On-demand. |
| Cover Art Validation | text_fast | Verifies that a downloaded cover image matches the expected work (catches mismatched covers from retail providers). |
| Audio Similarity | — | Chromaprint-based acoustic fingerprinting to detect duplicate audio files across formats. No LLM required. |

### Syncing (scheduled / on-demand)

| Feature | Models | What it does |
|---|---|---|
| Immersive Bake | audio + text_quality | Generates a synchronized audiobook/ebook experience — aligns audio timestamps to text positions. |
| Subtitle Sync | audio | Corrects subtitle timing drift using Whisper-generated transcription as ground truth. |
| Cross-Media Scene Mapping | text_quality | Maps equivalent scenes across book, film, and audiobook editions of the same work. |

### Personalization (background / on-demand)

| Feature | Model | What it does |
|---|---|---|
| Local Taste Profiling | text_quality | Builds a per-profile preference model from reading/watching history. Background service (Sunday 5 AM). |
| "Why" Factor | text_fast | Explains in plain language why a specific item was recommended to a user. |

### Discovery (user input)

| Feature | Model | What it does |
|---|---|---|
| Intent Search | text_fast | Translates a natural language search query ("something scary set in space") into structured filter parameters for the library query engine. |

### Advanced (manual)

| Feature | Model | What it does |
|---|---|---|
| User-Assisted URL Paste | text_quality | Extracts structured metadata from a pasted URL (publisher page, Wikipedia article, Goodreads link) when automated providers fail. |

---

## Relationship to the Priority Cascade

AI does not replace the Priority Cascade Engine. Wikidata remains the sole authority for canonical data. AI improves the quality of inputs to the cascade:

| AI feature | Cascade interaction |
|---|---|
| SmartLabeler | Cleans filenames before they are parsed into claims — better raw input for Tier D scoring |
| MediaTypeAdvisor | Emits a high-confidence `media_type` claim that enters the cascade as any other claim |
| QidDisambiguator | Accelerates Tier C resolution when multiple Wikidata candidates are returned |
| BatchManifestBuilder | Reduces the number of API calls, but does not change claim weights or cascade logic |

The cascade evaluates all claims — including those produced by AI features — using the same tier system.

---

## API Endpoints

| Method | Route | Purpose |
|---|---|---|
| GET | `/ai/status` | Model load status, memory usage |
| GET | `/ai/models` | List configured models with download status |
| POST | `/ai/models/{role}/download` | Trigger model download |
| POST | `/ai/models/{role}/load` | Load a model into memory |
| POST | `/ai/models/{role}/unload` | Unload a model |
| GET | `/ai/config` | Current AI configuration |
| POST | `/ai/enrich/tldr/{entityId}` | Generate TL;DR for a work |
| POST | `/ai/enrich/vibes/{entityId}` | Generate vibe tags for a work |
| POST | `/ai/enrich/search/intent` | Parse a natural language search query |
| POST | `/ai/enrich/extract-url` | Extract metadata from a pasted URL |

---

## Background Services

| Service | Schedule | Purpose |
|---|---|---|
| `ModelAutoDownloadService` | Startup | Downloads missing models to `/models` volume |
| `VibeBatchService` | Daily at 4 AM | Processes vibe tag queue for all un-tagged works |
| `SeriesAlignmentBackgroundService` | Daily at 3 AM | Resolves series order for works without Wikidata series position |
| `TasteProfileBackgroundService` | Weekly, Sunday at 5 AM | Rebuilds per-profile taste models from history |

---

## Configuration

All AI settings live in `config/ai.json`:

- Model definitions per role (path, quantization, context window)
- Per-feature enable flags
- Per-category vibe vocabularies (Books, Movies, Music, Podcasts, Comics)
- Scheduling parameters for background services
- Idle unload timeout
