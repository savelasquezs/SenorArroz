BEGIN;

ALTER TABLE delivery_tracking_incident
    ADD COLUMN IF NOT EXISTS alert_id bigint;

ALTER TABLE delivery_tracking_incident
    DROP CONSTRAINT IF EXISTS ck_delivery_tracking_incident_type;
ALTER TABLE delivery_tracking_incident
    ADD CONSTRAINT ck_delivery_tracking_incident_type
    CHECK (incident_type IN ('stay', 'route_deviation', 'location_disabled', 'tracking_interruption'));

CREATE UNIQUE INDEX IF NOT EXISTS uq_delivery_tracking_incident_alert
    ON delivery_tracking_incident(alert_id)
    WHERE alert_id IS NOT NULL;

COMMIT;
