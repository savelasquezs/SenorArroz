BEGIN;

ALTER TABLE branch
    ADD COLUMN IF NOT EXISTS delivery_tracking_auto_close_time time without time zone NOT NULL DEFAULT TIME '21:00:00',
    ADD COLUMN IF NOT EXISTS delivery_tracking_light_interval_seconds integer NOT NULL DEFAULT 300,
    ADD COLUMN IF NOT EXISTS delivery_tracking_active_interval_seconds integer NOT NULL DEFAULT 30,
    ADD COLUMN IF NOT EXISTS delivery_tracking_stay_threshold_minutes integer NOT NULL DEFAULT 10,
    ADD COLUMN IF NOT EXISTS delivery_tracking_stay_radius_meters integer NOT NULL DEFAULT 50,
    ADD COLUMN IF NOT EXISTS delivery_tracking_allowed_distance_meters integer NOT NULL DEFAULT 50,
    ADD COLUMN IF NOT EXISTS delivery_tracking_location_retention_days integer NOT NULL DEFAULT 3,
    ADD COLUMN IF NOT EXISTS delivery_tracking_incident_retention_days integer NOT NULL DEFAULT 15;

COMMIT;

-- Verificación opcional:
-- SELECT delivery_tracking_auto_close_time,
--        delivery_tracking_light_interval_seconds,
--        delivery_tracking_active_interval_seconds,
--        delivery_tracking_stay_threshold_minutes,
--        delivery_tracking_stay_radius_meters,
--        delivery_tracking_allowed_distance_meters,
--        delivery_tracking_location_retention_days,
--        delivery_tracking_incident_retention_days
-- FROM branch;
