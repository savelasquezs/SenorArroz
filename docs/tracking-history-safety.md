# Tracking history safety

Recovered locations retain their original timestamp, owner, work session and clientPointId. Deduplication and the closed-session capture window remain unchanged. A current location can be sent before a recovered batch.

A point older than a stored position does not invoke live geofence evaluation. The geofence service retains its existing two-minute age and GPS-quality validation. Route-clock repair, implicit assignment to the currently active route, and return-to-branch completion additionally require a point no more than two minutes old. Historical points are still persisted and notified; consumers must continue comparing recordedAt before moving the live marker.

No schema migration, token policy change, retention deletion or infrastructure setting is introduced. DeliveryTrackingHistoryTests covers fresh and stale returns, replay after a newer position, idempotence, and historical light tracking. Deploy this compatible API guard before enabling latest-first upload in delivery_app delivery 2.
