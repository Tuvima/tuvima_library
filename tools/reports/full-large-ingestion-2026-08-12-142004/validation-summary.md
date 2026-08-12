# Large ingestion and editor validation

Run completed on 2026-08-12 against the disposable harness roots at
`C:\temp\tuvima-watch` and `C:\temp\tuvima-library`.

## Reset and ingestion

- Deleted the development database locations and cleared both harness folders.
- Generated the current large corpus: 138 files.
- Detected and parsed 137 ingestible files.
- Registered 132 library items.
- Routed 3 items to review and 4 identity records to expected retail no-match outcomes.
- Completed 147/147 identity outcomes:
  - Ready: 70
  - Ready without universe: 73
  - Expected review/no-match: 4
  - Failed: 0
- Final active ingestion jobs: 0.
- Final failed ingestion jobs: 0.

The rebuilt database is
`C:\temp\tuvima-library\.data\database\library.db` (approximately 89 MiB at validation time).

## Runtime UI checks

- People editor exposes Details, Artwork, and History only.
- People editor has no Match tab.
- People Artwork uses the same rail, primary-artwork, upload, variants, details, preview, and removal workflow as media Artwork.
- Personal notes are absent from both media and people editors.
- Media retains its Match tab.
- Movie hero copy prefers Tagline and the rebuilt Home hero rendered the Aliens tagline, `This time it's war.`
- Rebuilt Home rendered populated Watch, Read, and Listen shelves.

## Automated checks

- Solution build: 0 warnings, 0 errors.
- Contract tests: 44/44 passed.
- Editor regression tests: 27/27 passed.
- Profile preference and fresh-schema tests: 4/4 passed.
- API success-metadata tests: 3/3 passed.
- `git diff --check`: passed.

Three broad suites retain four unrelated guardrail failures already present in
the surrounding enrichment work:

- Web: raw `GetFromJsonAsync` inventory is 117 against a ceiling of 116.
- Storage: the GUID-BLOB inventory fixture does not yet list the existing
  `enrichment_refresh_schedule` GUID columns.
- API: the existing enrichment refresh endpoint bypasses the paging clamp
  guardrail, and the existing enrichment refresh scheduler calls a raw
  transaction API.

None of those failing guardrails touches the editor, person artwork, person
matching removal, personal-notes removal, or the clean ingestion result.

## Logs

- `engine.stdout.log` contains the complete ingestion trace.
- `engine.stderr.log` contains standard-error output.
- `web.stdout.log` and `web.stderr.log` contain the rebuilt-dashboard smoke test.
