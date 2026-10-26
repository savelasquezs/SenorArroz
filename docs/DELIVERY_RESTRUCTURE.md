# DELIVERY-2026-10 — API

## Revisions
Flutter base 92ed306; API base 3fa9b06. Branch refactor/delivery-performance-offline in separate worktrees. Original dirty API Program.cs preserved. Prepublication qualification; no merge or production publication completed.

## Tests
- Flutter: 215 tests passed; latest startup/version/route/shared-action checks: 20 passed. Analyzer: 59 infos, no errors/warnings.
- Android Xiaomi: native independent SQLite connections/capture/ACK/read probe passed. The previous-process recovery probe also passed on the physical device.
- API: 1260 passed, 98 skipped in the full local suite. Latest real PostgreSQL checks: 5 passed, including receipt replay while at lunch and legacy repair twice/rollback. Additional work-session/return checks: 17 passed after serializing session reads with existing driver locks.
- Real local HTTP: atomic take A→B, rollback of invalid B, replay of A with current context, concurrent B/C from one lineage (one acceptance, one conflict), address token and delivery passed. This is development PostgreSQL, not production.

## Device evidence
POCO X7 Pro SC99BEFUGAR4CIHM, Android 15, profile builds, explicit localhost API via USB. Play installation replaced with prior user approval that there was no pending data.
- Baseline first tracking startup failed after 106301 ms with SQLite contention. Separate baseline reopen logs: version 193 ms, local session 590 ms, work session 1868 ms, tracking 907 ms. These are phase timings, not a statistically paired speed comparison.
- New profile first raster 129–200 ms from Dart main; this excludes process/engine launch. Useful cached route was observed. Tracking starts observed at 1616/1752 ms; service preparation is separate from Home.
- Airplane mode plus removed USB reverse: corrected address and confirmed delivery #42. Both UUIDs survived force-stop in real SQLite: address d500ff58-a9c3-41ca-92c0-e73edcb3fa78; delivery 8f11b2a9-cfb2-40ec-a43a-6488e70e1fde.
- Reopening exposed a 30 s wait caused by a two-minute lease from the terminated process. Android PID recovery passed unit/native checks and the physical repeat. Three online cold starts restored local identity in 635/655/670 ms. Offline cold start with address+delivery #45 restored both pending rows, identity 736 ms. Android launch measurements are separate (695–726 ms); these clocks overlap and must not be added.
- After retry/reconnection, each UUID has exactly one server receipt. Order delivered, additional info Casa offline 42; total 26000, delivery fee 5000, address fee 5000 and primary=true preserved. Route remains in_progress during return.
- The Android integration-test harness uninstalls its APK on completion (Flutter IntegrationTestDevice.kill). Do not run it on an installation with real pending data. Final profile APK was restored; subsequent ordinary force-stop/reopen preserved pending operations. Use the emulator for further harness runs.
- Empty HTTP 204 work-session responses now clear stale session cache instead of exposing a String-to-Map cast. Version checks run independently of preparation; HTTP 426 still enforces updates.
- Second physical offline acceptance #45: address aa7c7122-66c1-4f5c-ba34-a53c9dd3972c and delivery fad12b7f-115f-467b-ad35-e5bd8bbeedd2 survived cold reopen. An ordinary adb install -r of the same profile APK retained them. Reconnection produced one receipt each, delivered status, Casa-offline-45, total 23500, order/address fee 2500, primary=true. This proves same-build reinstall retention; a Play-version migration remains separate.
- Screen-off test: capture continued at the configured active interval (30 s) and background reconnection accepted both operations even while Android was locked. Longer standby/cadence qualification remains pending.
- Disk exhaustion interrupted a Flutter run and Docker. Only task-generated builds were cleaned using flutter clean. Docker was restarted with explicit user approval; full Flutter tests and the PostgreSQL preflight were repeated successfully afterward.

## Production read-only preflight
95 active routes; 9 owners with duplicates (94 rows), no pending orders in the duplicate set at inspection. New columns absent. The script audits and closes only superseded terminal routes, clears inferred physical durations, and rolls back if unresolved duplicates remain. Production has not been modified.

## Code balance
Handwritten changes excluding docs, both repositories: approximately +682 lines (Flutter +230, API +452; pending final recount). Includes tests, SQL and the local HTTP acceptance script. Negative total target is not achieved. Removed manual return UI/flow, legacy test-only loading, duplicate contact/actions, duplicated address patch/payload and obsolete full-address client update. No second database or synchronization framework.


## Pending
- Account/session/conflict/storage physical qualification, longer screen-off GPS/backlog qualification and Play upgrade retention. Restart/offline pending UI, #45 reconnect exact-once and ordinary same-build reinstall retention passed on device.
- Complete comparable profile measurements, zero history/metrics requests before visit, and F01–F48 regression qualification.
- Reduce the remaining code delta without removing useful coverage; finalize native/analyzer/backend/PostgreSQL checks.
- Reviewable commits/PRs, remote CI, production schema/backend compatibility checks, then coordinated Play release and installed-build/cold-start validation. No publication is complete.
