---
title: "How the Local AI Works"
summary: "Understand the local models, hardware tiers, and AI responsibilities inside Tuvima Library."
audience: "user"
category: "explanation"
product_area: "ai"
tags:
  - "ai"
  - "local-models"
  - "hardware"
---

# How the Local AI Works

Tuvima Library's AI is not a cloud feature with a monthly cost and a privacy disclaimer. It runs entirely on your machine, using your CPU and GPU, processing your files without any data ever leaving your home. This page explains what the AI does, how it's designed, and â€” equally important â€” what it doesn't do.

---

## Fully Local, Always

Every AI model in Tuvima Library runs on your hardware:

- No cloud APIs
- No subscriptions
- No data transmitted externally
- No usage tracking

Models download automatically on first startup (about 9 GB total for a standard setup). After that, the AI works offline indefinitely.

This isn't just a privacy nicety â€” it's what makes the AI viable for enriching a large personal collection over time. Cloud AI APIs have rate limits, costs, and latency. Local inference has none of those constraints. The Engine can process your entire library continuously in the background without you ever seeing a bill.

---

## Four Models, Four Jobs

The AI layer uses multiple specialized models rather than one model doing everything. Smaller models are faster and cheaper to run; larger models produce richer analysis. The right tool for the job:

| Model | Size | Role | What it handles |
|---|---|---|---|
| Llama 3.2 1B | ~750 MB | Quick tasks | Media type classification, search query parsing, on-demand requests |
| Llama 3.2 3B | ~1.9 GB | Ingestion | Filename cleaning, Wikidata candidate matching, vibe tag generation |
| Llama 3.1 8B | ~4.6 GB | Deep enrichment | Theme/mood/setting/pace extraction, TL;DR summaries, character name extraction |
| Whisper Medium | ~1.5 GB | Audio | Transcription, language detection, subtitle synchronization |

There's also an optional fifth model: **Qwen 2.5 3B** for CJK (Chinese, Japanese, Korean) language processing. It downloads automatically if you have CJK languages configured in your preferences â€” you don't need to think about it.

The 1B model handles anything that needs to be fast and on-demand. The 3B handles batch work during ingestion. The 8B handles the deep enrichment that runs in the background over time. Whisper is in its own category entirely â€” it's a speech-to-text model rather than a language model.

---

## Hardware Auto-Profiling

Not every machine has the same capabilities. On first startup, the Engine benchmarks your CPU and GPU and classifies your hardware into one of three tiers:

**High** (e.g., RTX 3060 or better)
- All AI features available
- Continuous background enrichment
- 8B model available
- GPU acceleration via CUDA (NVIDIA) or Vulkan (AMD/Intel Arc)

**Medium** (e.g., i7, 16 GB RAM)
- All AI features available
- Scheduled enrichment (runs during quiet periods)
- GPU used if available; falls back to CPU

**Low** (e.g., i5, 8 GB RAM)
- All features available, but heavy features run overnight only
- No 8B model (too slow to be useful)
- CPU only

GPU detection is automatic. CUDA is tried first for NVIDIA cards. Vulkan is used for AMD and Intel Arc. Integrated GPUs are intentionally left alone â€” the Engine uses those for video transcoding, so AI takes the discrete GPU and leaves integrated graphics to FFmpeg.

You can see your hardware profile and current tier at Settings â†’ Intelligence â†’ Models.

---

## Resource Awareness

The AI doesn't just run blindly whenever it wants. It monitors the system actively:

- **CPU load**: If CPU usage is high (e.g., another application is working hard), AI enrichment pauses
- **RAM pressure**: If available RAM drops below a threshold, AI backs off
- **Active transcoding**: If FFmpeg is encoding a video, the AI defers heavy models until the encode completes

This means Tuvima Library coexists gracefully with the rest of your system. You won't notice the AI working in the background unless you go looking for it. And if you're doing something CPU-intensive, the library waits.

---

## Constrained Output with GBNF Grammars

One of the practical problems with using language models in a production system is that they can produce unpredictable output. Ask a model to return JSON and it might return JSON with an explanation attached, or malformed JSON, or JSON with hallucinated fields.

Tuvima Library solves this with **GBNF grammar constraints**. Every AI call that needs structured output (a JSON response, a list, a classification) uses a grammar that constrains the model to produce only valid output in exactly the format expected. The model cannot produce anything outside the grammar.

This eliminates parsing failures. The Engine never has to defensively handle malformed AI output because the model is literally incapable of producing it.

---

## What the AI Does and Doesn't Do

This is the most important section to understand.

### What the AI does

The AI is a **quality improvement layer** for matching and classification:

- **SmartLabeler**: Cleans up messy filenames before matching. "Frank.Herbert.Dune.Part.1.EPUB.rip" becomes "Dune Part 1 by Frank Herbert" â€” a much better search query.
- **QidDisambiguator**: When Wikidata returns multiple candidates for the same title, the AI picks the right one by comparing descriptions and properties against the file's metadata.
- **MediaTypeAdvisor**: Classifies ambiguous formats (MP3 could be music or audiobook; MP4 could be a film or a TV episode).
- **VibeTagger**: Generates 25â€“30 vibe tags per item based on descriptions and themes.
- **DescriptionIntelligenceService**: Extracts structured vocabulary from descriptions â€” themes, mood, setting, pace, TL;DR, character names.
- **IntentSearchParser**: Translates natural language search queries ("something scary set in space with a cozy feel") into structured search filters.
- **Whisper transcription**: Reads audio content, detects languages, and syncs subtitles.

### What the AI doesn't do

The AI **does not replace the Priority Cascade**. It doesn't determine canonical values for title, author, genre, series, or any other factual metadata. That's Wikidata's job.

This distinction matters. The AI can make matching better â€” help the Engine find the right Wikidata match for an item, help it classify an ambiguous file. But once the match is found, Wikidata's structured data flows through the Priority Cascade to determine what the canonical values are. The AI is an input to the matching process, not the authority for the output.

---

## Genre vs Vibes

These are two related but distinct concepts worth understanding clearly.

**Genres** come from Wikidata and retail providers. They describe *what something is* in a categorical sense: science fiction, biography, mystery, documentary. Genres are relatively stable, well-defined, and sourced from authoritative data.

**Vibes** are AI-generated and describe *how something feels*. Each media type has a vocabulary of 25â€“30 vibe tags:
- For books: atmospheric, cerebral, cozy, haunting, propulsive, intimate, sweeping, spare
- For films: kinetic, contemplative, tense, warm, bleak, playful, operatic
- And so on

Vibes aren't categorical â€” they're emotional texture. A book can be both "atmospheric" and "propulsive". A film can be "kinetic" and "haunting".

**Intent Search** combines both. When you type "something scary set in space", the Engine parses "scary" as a vibe signal (maps to "tense", "haunting") and "space" as a genre/setting signal. The search returns items that match both dimensions â€” not just genre:science-fiction, but specifically the items in your library that feel scary.

---

## Description Intelligence

This runs as a background batch service on a 15-minute schedule. It doesn't run inline during ingestion â€” that would slow everything down. Instead, after a file is ingested and its descriptions have been fetched from providers, the Description Intelligence service processes those descriptions asynchronously.

What it extracts:
- Themes (philosophical or narrative â€” "mortality", "power and corruption")
- Mood (emotional register â€” "melancholic", "hopeful")
- Setting (time and place â€” "near-future", "rural England")
- Pace (narrative speed â€” "slow burn", "relentless")
- TL;DR (a one-sentence summary)
- Character names (for cross-linking with the Universe Graph)

This data feeds the vibe tagging, the Intent Search index, and the Universe Graph's character detection.

---

For technical details about the AI model architecture, hardware benchmark thresholds, GBNF grammar definitions, the feature policy matrix by hardware tier, and the full list of 16 AI features across 7 categories â€” see the [architecture deep-dive](../architecture/ai-integration.md).

## Related

- [Local AI Intelligence Layer](../architecture/ai-integration.md)
- [How to Set Up Language Preferences](../guides/language-setup.md)
- [How Two-Stage Enrichment Works](how-hydration-works.md)
