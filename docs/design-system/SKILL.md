---
name: tuvima-design
description: Use this skill to generate well-branded interfaces and assets for Tuvima Library, either for production or throwaway prototypes/mocks/etc. Contains essential design guidelines, colors, type, fonts, assets, and UI kit components for prototyping.
user-invocable: true
---

Read the `README.md` file within this skill, and explore the other available files — `colors_and_type.css` for design tokens, `assets/` for logos and icons, `fonts/` for self-hosted Montserrat + Merriweather + JetBrains Mono, `preview/` for token specimens, and `ui_kits/dashboard/` for a full clickable recreation of the cinematic library browser (home + collection detail).

Tuvima Library is a dark-only, cinematic media-library product. The visual language is deep navy + glassmorphic panels + a single golden amber accent (`#EAB308` bright, `#C9922E` deep). Montserrat is the UI face; Merriweather is scoped to the EPUB reader only. Iconography is FontAwesome solid — no emoji, ever.

If creating visual artifacts (slides, mocks, throwaway prototypes, etc.), copy assets out of this folder and create static HTML files that link `colors_and_type.css` for tokens. If working on production code, copy assets and read the rules in `README.md` to become an expert in designing with this brand — pay special attention to the CONTENT FUNDAMENTALS (voice, casing, person), VISUAL FOUNDATIONS (shadows, blur, transparency rules), and ICONOGRAPHY sections.

If the user invokes this skill without any other guidance, ask them what they want to build or design, ask some focused questions (audience, surface — dashboard vs reader, hi-fi vs wireframe, how many variations), and act as an expert designer who outputs HTML artifacts _or_ production code, depending on the need.

## Current Dashboard/Product UI Model

Home, Read, Watch, Listen, and Search are the user-facing discovery and media surfaces. Detail pages and media rows/cards launch inline editing through the shared media editor. Review Queue is only for blocked or uncertain items that need human confirmation. Settings/Admin is for configuration and operational/system concerns. The old Vault concept is deprecated and must not be recreated; do not add new Vault routes, components, docs, or management-workbench flows.

Design changes must preserve the Phase 4 quality gates: no retired Vault/LibraryPage workflow, no Vault navigation label, and media correction controls should launch the shared editor instead of creating a separate management surface.


