# Tracking alert release checks — 2026-10-08

Release order: compatible API, administrative frontend, then delivery_app through the existing Google Play internal pipeline. No schema migration or sampling change is required. Do not enable the explicitly deferred active_delivery + active-route eligibility gate.

## Regression corrections

The previous full backend run had six failing cases. Two still expected the old 50-metre stay radius; only stay radius changes to 20 metres, not branch or delivery geofence tolerances. One expected InternetAvailable=true when the request had no observed network state; the contract preserves null/true/false explicitly. Two expected the browser/server clock to add minutes to a stay without further samples; duration and endedAt now remain bounded by recorded evidence, and IsActive expires after the cadence allowance. Tests cover the exact light-cadence boundary (450 seconds) and the first stale second (451 seconds).

The combined alert test used a clock earlier than the stay evidence it seeded. Its clock now progresses after the evidence and the repeated-pass assertion still requires zero changes and no duplicate FCM. The recovery assertion uses the first actual server receipt, rather than the next worker scan or later session heartbeat. No assertion is removed to accept duplicate notifications or unsupported evidence.

## Required gates

- Full backend build and tests, including configured PostgreSQL integration tests.
- Frontend build and complete test suite.
- Flutter analysis/tests and Android native evidence tests on the final PR head.
- No temporary transfer files or write-enabled preparation workflows in merged trees.
- Verify API/frontend deployments against the merged commit, not merely a successful historical deployment.
- Verify the signed App Bundle's successful Google Play internal edit commit and versionCode.

## Field validation

Update the existing installation without deleting local data. On real phones verify 59/60-second GPS-off boundary, independent permission changes, screen-locked capture and token renewal, recovered communication history, and normal end of shift. A native unit test or successful store upload does not establish an actual device's behavior or delivery/read receipt of a push. Administrative review remains mandatory; no automatic sanction.
