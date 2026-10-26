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

## F01–F48 regression qualification
Evidence is scoped to the layer named. A source-preserved path or passing unit test does not claim a physical end-to-end pass. Every outstanding smoke below remains a publication qualification item.

| ID | Function | Verified evidence | Remaining qualification |
|---|---|---|---|
| F01 | Login/restauración: mejorar | Startup/version tests; cold profile 635–786 ms local identity; offline 736 ms | Paired baseline/profile repetitions |
| F02 | Recuperar/cambiar contraseña: conservar y pulir | Auth repository/forms preserved; profile password form opens | Submit/change/recovery smoke |
| F03 | Actualización por Play: conservar | Version/426 tests and Play distribution policy checks | Play installed build/version and store failure smoke |
| F04 | Jornada: mejorar | 17 session/return checks; physical start/reopen session 82 | Physical cutoff, account replacement and logout recovery |
| F05 | Ubicación/batería/permisos: mejorar | Location policy tests; physical precise/background permissions | Permission revocation and battery-management smoke |
| F06 | Navegación principal: mejorar | Physical four tabs; HTTP no history/dashboard before first visit; IndexedStack/TickerMode | Filter/scroll retention over longer navigation |
| F07 | Perfil/foto/contacto: mejorar | Physical profile known identity/email/phone; existing profile flow retained | Photo/update failure smoke |
| F08 | Disponibles por barrio: mejorar | Available layout/realtime tests; physical neighborhood grouping | Large real route selection smoke |
| F09 | Urgencia: conservar y aclarar | Visible urgency implementation retained; hidden timer uses TickerMode | Visible/hidden timer trace |
| F10 | Preparación cerca de sede: conservar | Existing readiness/proximity backend checks; take rejects invalid context | Physical boundary/freshness cases |
| F11 | Propuestas y esperar/salir: simplificar | Routing model/validity/refresh tests; selection contract retained | Wait/leave proposal visual smoke |
| F12 | Editor de propuesta: conservar | Proposal editor tests and golden regression passed | Rapid edit/close on device |
| F13 | Tomar pedidos/ruta: sustituir regla previa | Real local HTTP A→B, invalid B rollback, concurrent lineage winner | Repeat combined GPS/take race in real PostgreSQL |
| F14 | Ruta activa: mejorar | PostgreSQL uniqueness/context tests; physical cached route and return; stale response tests | Long-delayed cross-session GPS test |
| F15 | Próxima entrega/reservas: conservar | Reservation prioritization/order helper tests retained | Reservation timing physical smoke |
| F16 | Entrega manual: mejorar | Confirmation/storage-failure test; physical offline delivery #45 | Ambiguous response loss physical test |
| F17 | Autoentrega GPS en destino: conservar | PostgreSQL concurrent GPS exactly-once test with stubbed status transition | Full GPS visit/departure field qualification |
| F18 | Regreso y pendientes: sustituir regla previa | Return recovery tests and common batch implementation; manual UI removed | Full physical observed return scenario |
| F19 | Detalle/productos/notas: mejorar | Physical known-data detail works in airplane mode | Large notes/products overflow smoke |
| F20 | Pagos/saldo/Ya pagó: mejorar | Physical #42/#45 totals/fees/primary unchanged; original payment paths retained | Mixed/partial payment end-to-end regression |
| F21 | Maps destino: conservar | Destination maps helper tests retained | External app/open failure smoke |
| F22 | Maps varias paradas: mejorar | Route destination order/link implementation retained | Multi-stop exclusion/limit visual smoke |
| F23 | Llamada/WhatsApp: mejorar | WhatsApp normalization tests; shared contact helpers | External call/chat opening without sending |
| F24 | Edición de dirección: corregir | Physical durable offline corrections, receipt and unchanged fees/primary | Physical conflicting correction review |
| F25 | Selector GPS/mapa/enlace: corregir | Direct GPS button independent from map; picker/parser preserved | GPS timeout/close lifecycle physical smoke |
| F26 | Reimpresión: conservar | Shared reprint action and print model tests; existing backend job queue | Printer acceptance smoke |
| F27 | SignalR: conservar y ajustar | Authoritative catchup/stale response tests; physical connected hub | Dropped websocket and delayed old event qualification |
| F28 | GPS activo/liviano: conservar | Native capture/ACK concurrency passed; screen-off active interval 30 s | Long standby cadence/power trace |
| F29 | Pantalla apagada/reinicio/actualización: conservar y medir | Force-stop/reopen and ordinary same-build install-r retain queue | Play upgrade and device reboot qualification |
| F30 | Outbox offline: conservar | SQLite owner/UUID/ACK tests; physical durable address+delivery exact UUIDs | Legacy installation migration with backlog |
| F31 | Backlog: mejorar | Existing scheduler/outbox tests and native parallel capture/ACK | Large historical backlog physical trace |
| F32 | Refresh UI/GPS: conservar y simplificar | Session mutex/auth sync tests plus native previous-PID recovery | Concurrent expiring credentials physical trace |
| F33 | Reintentos/429: conservar | Backoff and HTTP-date Retry-After paths retained/tested | Real server 429/reconnect storm smoke |
| F34 | Evidencia GPS apagado: conservar | Tracking alert evidence tests retain continuous-minute rules | Physical GPS disabled/revoked-permission evidence |
| F35 | Salud del tracking: mejorar | Tracking status tests and physical offline/reconnect indicators | Extended capture/upload degraded-state smoke |
| F36 | Cuarentena: mejorar | Typed quarantine/outbox tests preserve rows and ACK isolation | Physical quarantine repair/review |
| F37 | Push/alertas: mejorar | Notification dedup/review tests; owner/session hints scoped | Real FCM stale-session delivery smoke |
| F38 | Historial/paginación: mejorar | Physical history loads 7 fixture deliveries only on visit; request generations | Rapid filters/pagination response race smoke |
| F39 | Ganancias por sucursal: conservar y aclarar | Physical history fee share and metrics totals; existing payroll contracts retained | Cross-branch period reconciliation smoke |
| F40 | Métricas/gráficas/rutas: simplificar | Inferred-source duration exclusion; repository projection regression test | Recheck corrected projection on physical metrics |
| F41 | Nómina/préstamos/abonos: conservar y aclarar | Existing payroll/advances/backend tests retained; metric period visible | Loan/advance settlement visual smoke |
| F42 | Almuerzo: conservar y mejorar | Lunch tests and real PostgreSQL serialization; receipt replay during lunch | Physical password/restriction/error flows |
| F43 | Tipos de permisos: conservar | Employee leave tests and catalog/authorization paths retained | Paid/unpaid type form smoke |
| F44 | Flujo de permisos: conservar y optimizar | Leave replay/cleanup tests; request/edit/cancel paths retained | Physical draft/version conflict flows |
| F45 | Adjuntos privados: conservar | Multipart replay/cleanup tests; private attachment API retained | Physical upload/progress/retry/privacy smoke |
| F46 | Documentos/búsqueda/descarga: conservar y optimizar | Document model tests; stale response generation filter | Physical search/download/open errors |
| F47 | Errores/diagnóstico: mejorar | User-facing error tests; HTTP204 regression and recoverable restore | Final release log/diagnostic review |
| F48 | Tema/componentes: mejorar | Layout/golden tests; common targets minimum48dp | Light theme, text scaling and contrast physical checks |
