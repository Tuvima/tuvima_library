# Crash verification before Access refactoring

Verified on 2026-09-08 before Access implementation. This note separates observed failures from symptoms that still need reproduction. It does not claim that the application can no longer crash.

## Evidence reviewed

- `logs/codex-crash-diagnosis-engine.err.log` and `logs/codex-crash-diagnosis-dashboard.err.log` are empty.
- `logs/codex-crash-diagnosis-engine.out.log` shows the Engine listening at 20:44:50 and continuing periodic work through 21:54:51. It contains no fatal, unhandled, or shutdown entry.
- `logs/codex-crash-diagnosis-dashboard.out.log` shows the Dashboard listening and continuing through 21:41:24. Its repeated 403 responses are caught `HttpRequestException` warnings after the authenticated session became invalid; there is no `CircuitHost`, renderer, fatal, or unhandled entry in that capture.
- Windows Application events from `.NET Runtime` contain one Dashboard circuit failure on 2026-09-08 at 15:43:35. `DashboardServiceCredentialProvider.GetToken()` threw because `src/MediaEngine.Web/config/.secrets/dashboard-engine.credential.json` was missing. The reported path indicates this launch did not resolve the repository `../../config` launch-setting path.
- Windows Application event 1026 records one Engine process termination on 2026-09-08 at 16:04:34. `DashboardServiceCredentialBootstrapper.EnsureAsync()` rejected a credential bundle whose token did not match the Engine database.
- Windows Application event 1026 records two Engine startup terminations on 2026-09-06 at 15:45:18 and 15:45:52. `StorageEpochGuard.BackupAndRemove()` could not move an obsolete database because the file was in use. This proves a locked-file startup failure, but the event itself does not identify which process held the file.
- The earlier SQLite read-only startup failure was produced by a sandbox-restricted validation launch. It is evidence about that launch environment, not a normal application crash.
- `logs/tuvima-20260906.log` contains six unhandled request exceptions at 15:33 caused by obsolete state missing `user_status.revision`. Each request returned HTTP 500; these are historical request failures, not evidence that either host process exited. They do not recur in the 2026-09-07, 2026-09-08, or crash-diagnosis captures.

## Implemented correction and verification

The missing-credential circuit stack exposed a current source defect: `Program.cs` loaded the credential while constructing HTTP clients and `ViewProfileAssertionHandler`. A missing or unreadable bundle could therefore escape before page-level error handling and terminate the circuit. The credential provider also cached a successfully loaded token for the Dashboard process lifetime, so an Engine credential rotation could leave it sending a stale token until restart.

The Dashboard now loads and validates the credential at request send time. Missing, malformed, or undecryptable state produces one warning per changed credential state and a local HTTP 503 without sending an unauthenticated upstream request. The small credential bundle is content-hashed on each request, which detects rotation even when replacement preserves file size and timestamp. View request signatures use the same current token, and the artwork proxy keeps its existing service-only authentication behavior. Focused tests cover missing state, malformed JSON, protection with the wrong key ring, recovery when the file appears, rotation, and refusal to reuse a previously cached token.

The live smoke check exposed a second path: the sign-in endpoint treated an unavailable Engine as an unhandled bootstrap GET failure. Identity GETs now preserve unknown status on HTTP, transport, or timeout failure. Sign-in renders a retry page with HTTP 503; only an explicit unconfigured bootstrap result opens first-time setup. This avoids accidentally offering setup during an outage.

Final verification: the solution build passed and all 3,308 tests passed, with 37 existing provider integration skips. Twelve focused credential/first-run tests cover recovery, rotation, unknown bootstrap status, and execution of the actual retry HTTP result. An isolated live Dashboard with no credential returned the retry page for `/auth/login` and its ingestion-return variant while `/health/live` stayed HTTP 200; stderr was empty and the smoke capture had no unhandled/fatal entries. Evidence: `logs/access-prerequisite-final-tests.log`, `logs/access-credential-smoke-results.json`, and `logs/access-credential-smoke-final.*.log`. The temporary server was stopped after verification.

The available evidence still supports several distinct startup/state failures and one historical request-schema failure. It does not support the broader claim that duplicate launches alone caused every reported crash.

The actual retry page was also visually inspected at 1920×1080 and 390×844 using a separate temporary Dashboard on port 5117 with an absent credential. It displayed the existing authentication shell, an Engine unavailable explanation, and a readable Try again action without setup controls. The action retained the ingestion return URL. The test tab and temporary server were closed afterward; the normal Engine and Dashboard remained running. Logs: `logs/access-retry-visual.*.log`. Screenshots were captured inline in this task.

To classify any remaining crash, record the time and whether the browser showed a reconnect/error screen, the Engine or Dashboard process exited, or the Codex desktop app closed. Correlate that time with Windows Application `.NET Runtime` events and both host logs before changing source. A successful single Engine/Dashboard observation reduces immediate concern but is not a recurrence guarantee.

Plain English: the Dashboard will now stay responsive when its private Engine credential is briefly missing or replaced; it reports the Engine as unavailable and retries safely on the next request. Other verified failures involved disposable development database state or a locked database, so the later clean run is still not a guarantee that every separately reported crash is fixed.

A September 9 follow-up checked the same normal processes without restarting them: both Engine and Dashboard `/health/live` returned 200; both stderr files remained empty, and their current stdout logs contained no unhandled-exception, fatal, or `fail:` matches. Evidence: `logs/access-runtime-followup-health.json`. This longer observation supports the recovery fix but does not extend coverage to unobserved failure modes.
