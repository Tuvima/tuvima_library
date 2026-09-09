# Access runtime verification

## Initial disposable smoke — 2026-09-09

This is provisional verification, not acceptance of the full Access cutover.

- Built the isolated integration Engine and Dashboard after the typed system-service admission checkpoint. Both builds passed with zero warnings and errors.
- Runtime state is under `.tmp/access-integration/.tmp/access-runtime-qa`. Configuration was copied from tracked templates, then given an empty library list, exclusively disposable storage paths, disabled metadata providers, disabled AI downloads/features, disabled discovery, and no external authentication providers. No original database, credentials, media, or configured source directories were copied.
- Engine listens on `http://127.0.0.1:61515`; Dashboard on `http://127.0.0.1:5116`. These are development test processes, separate from normal ports.
- Browser verification completed first-run administrator creation and sign-in using a synthetic test account, deferred media locations, completed setup, and rendered the empty authenticated Home surface.
- Both `/health/live` endpoints returned HTTP 200. The smoke logs had no `Unhandled`, `[FTL]`, or `fail:` entries at the check. Evidence: integration `logs/access-runtime-qa-health.json` and `logs/access-runtime-qa-{engine,dashboard}.{out,err}.log`.
- **Open P05 blocker:** direct navigation to `/settings/access/users` redirected the newly authenticated administrator to `/settings/profile`. Administration was absent from the interactive navigation despite successful server authentication. The suspected boundary is request-scoped cookie validation versus circuit-scoped authority initialization, compounded by route normalization before authority loading. Terra owns the fix and a first-navigation regression test; root will repeat the browser check on the corrected build.

The synthetic test password and recovery codes are intentionally not retained in this document. This smoke does not establish catalogue, View, provider, plugin, event, telemetry, or webhook acceptance.

The two disposable QA processes were stopped before integrating the next code checkpoints. The regular Engine and Dashboard were then restored from the existing verified builds on ports 61495 and 5016. Both liveness checks returned HTTP 200 at 08:32 local time; their checked logs had no fatal/unhandled entries. Evidence is in the original checkout at `logs/access-normal-resumed-health.json` and `logs/access-normal-resumed-{engine,dashboard}.{out,err}.log`. The regular runtime still uses the original architecture; it is not evidence that the new Access cutover is complete.

## Direct Access navigation corrected

The initial circuit seeding and validation coalescing fixes did not fully resolve direct navigation. Runtime diagnostics showed successful live administrator validation followed by `ActiveProfileSessionService.ResolveActiveProfileAsync` overwriting that authority with a null projection from the retained cookie principal. Profile presentation loading now preserves the current session and authority, including a more recent profile switch.

- The independent profile, authority, Settings navigation, and shell suite passed **80/80**. The new regression checks that profile load/refresh preserves validated actions, navigation, session revision, and the current profile when the cookie names an older profile. The old concurrent profile test now supplies an explicit authenticated identity rather than assuming a seed-profile fallback.
- Fresh browser document navigations to `/settings/access/users` and `/settings/access/applications` remained on the requested route and rendered their authenticated content. This verifies the actual runtime failure, beyond the earlier component tests.
- Evidence: `logs/access-profile-authority/profile-authority-preservation.trx` and `logs/access-qa-p05-profile-preserved-{engine,dashboard}.{out,err}.log` in integration. This resolves the first-navigation blocker; complete Users/Applications workflows and the wider security gates remain open.
