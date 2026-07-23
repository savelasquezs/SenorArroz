BEGIN;

ALTER TABLE delivery_tracking_alert
    ADD COLUMN IF NOT EXISTS recovery_device_event_id bigint,
    ADD COLUMN IF NOT EXISTS recovered_at timestamp with time zone,
    ADD COLUMN IF NOT EXISTS duration_seconds integer,
    ADD COLUMN IF NOT EXISTS start_latitude numeric(10,6),
    ADD COLUMN IF NOT EXISTS start_longitude numeric(10,6),
    ADD COLUMN IF NOT EXISTS start_location_recorded_at timestamp with time zone,
    ADD COLUMN IF NOT EXISTS end_latitude numeric(10,6),
    ADD COLUMN IF NOT EXISTS end_longitude numeric(10,6),
    ADD COLUMN IF NOT EXISTS end_location_recorded_at timestamp with time zone;

CREATE INDEX IF NOT EXISTS idx_delivery_tracking_alert_recovery_event
    ON delivery_tracking_alert(recovery_device_event_id);

WITH recovery_candidates AS (
    SELECT
        alert.id AS alert_id,
        event.id AS event_id,
        event.recorded_at,
        ROW_NUMBER() OVER (
            PARTITION BY alert.id
            ORDER BY event.recorded_at, event.id
        ) AS row_number
    FROM delivery_tracking_alert AS alert
    JOIN delivery_device_event AS event
      ON event.work_session_id = alert.work_session_id
     AND event.recorded_at >= alert.occurred_at
     AND (
        (alert.alert_type = 'gps_disabled' AND event.event_type = 'gps_enabled')
        OR
        (alert.alert_type = 'location_permission_revoked' AND event.event_type = 'location_permission_recovered')
     )
    WHERE alert.alert_type IN ('gps_disabled', 'location_permission_revoked')
)
UPDATE delivery_tracking_alert AS alert
SET recovery_device_event_id = recovery.event_id,
    recovered_at = recovery.recorded_at,
    last_occurred_at = recovery.recorded_at,
    duration_seconds = GREATEST(
        0,
        EXTRACT(EPOCH FROM recovery.recorded_at - alert.occurred_at)::integer
    )
FROM recovery_candidates AS recovery
WHERE recovery.alert_id = alert.id
  AND recovery.row_number = 1
  AND alert.recovered_at IS NULL;

WITH nearest_start AS (
    SELECT DISTINCT ON (alert.id)
        alert.id AS alert_id,
        location.latitude,
        location.longitude,
        location.recorded_at
    FROM delivery_tracking_alert AS alert
    JOIN deliveryman_location AS location
      ON location.work_session_id = alert.work_session_id
     AND location.recorded_at <= alert.occurred_at
    WHERE alert.alert_type IN ('gps_disabled', 'location_permission_revoked')
    ORDER BY alert.id, location.recorded_at DESC, location.id DESC
)
UPDATE delivery_tracking_alert AS alert
SET start_latitude = location.latitude,
    start_longitude = location.longitude,
    start_location_recorded_at = location.recorded_at
FROM nearest_start AS location
WHERE location.alert_id = alert.id
  AND alert.start_latitude IS NULL;

WITH nearest_end AS (
    SELECT DISTINCT ON (alert.id)
        alert.id AS alert_id,
        location.latitude,
        location.longitude,
        location.recorded_at
    FROM delivery_tracking_alert AS alert
    JOIN deliveryman_location AS location
      ON location.work_session_id = alert.work_session_id
     AND location.recorded_at >= alert.recovered_at
    WHERE alert.alert_type IN ('gps_disabled', 'location_permission_revoked')
      AND alert.recovered_at IS NOT NULL
    ORDER BY alert.id, location.recorded_at, location.id
)
UPDATE delivery_tracking_alert AS alert
SET end_latitude = location.latitude,
    end_longitude = location.longitude,
    end_location_recorded_at = location.recorded_at
FROM nearest_end AS location
WHERE location.alert_id = alert.id
  AND alert.end_latitude IS NULL;

UPDATE delivery_tracking_alert AS alert
SET duration_seconds = incident.duration_seconds,
    start_latitude = incident.center_latitude,
    start_longitude = incident.center_longitude,
    start_location_recorded_at = incident.started_at
FROM delivery_tracking_incident AS incident
WHERE alert.incident_id = incident.id
  AND alert.alert_type = 'unexpected_stay';

UPDATE delivery_tracking_alert
SET status = 'active',
    resolved_at = NULL,
    resolved_by_user_id = NULL,
    resolution_reason = NULL,
    updated_at = now()
WHERE alert_type IN ('gps_disabled', 'location_permission_revoked')
  AND resolution_reason = 'Recuperada automáticamente por evento del dispositivo.';

COMMIT;
