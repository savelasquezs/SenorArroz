BEGIN;

ALTER TABLE delivery_tracking_incident
    ADD COLUMN IF NOT EXISTS alert_id bigint,
    ADD COLUMN IF NOT EXISTS interruption_cause varchar(60),
    ADD COLUMN IF NOT EXISTS interruption_certainty varchar(40);

ALTER TABLE delivery_device_event
    ADD COLUMN IF NOT EXISTS offline_location_count integer,
    ADD COLUMN IF NOT EXISTS offline_started_at timestamp with time zone,
    ADD COLUMN IF NOT EXISTS offline_ended_at timestamp with time zone;

ALTER TABLE delivery_incident_device_event_evidence
    ADD COLUMN IF NOT EXISTS offline_location_count integer,
    ADD COLUMN IF NOT EXISTS offline_started_at timestamp with time zone,
    ADD COLUMN IF NOT EXISTS offline_ended_at timestamp with time zone;

ALTER TABLE delivery_tracking_incident
    DROP CONSTRAINT IF EXISTS ck_delivery_tracking_incident_type;
ALTER TABLE delivery_tracking_incident
    ADD CONSTRAINT ck_delivery_tracking_incident_type
    CHECK (incident_type IN ('stay', 'route_deviation', 'location_disabled', 'tracking_interruption'));

ALTER TABLE delivery_tracking_incident
    DROP CONSTRAINT IF EXISTS ck_delivery_tracking_incident_interruption_cause;
ALTER TABLE delivery_tracking_incident
    ADD CONSTRAINT ck_delivery_tracking_incident_interruption_cause CHECK (
        interruption_cause IS NULL OR interruption_cause IN (
            'gps_disabled', 'location_permission_revoked', 'airplane_mode_enabled',
            'app_or_tracking_service_stopped', 'wifi_disabled', 'connectivity_interruption',
            'device_restarted', 'not_determined'
        )
    );

ALTER TABLE delivery_tracking_incident
    DROP CONSTRAINT IF EXISTS ck_delivery_tracking_incident_interruption_certainty;
ALTER TABLE delivery_tracking_incident
    ADD CONSTRAINT ck_delivery_tracking_incident_interruption_certainty CHECK (
        interruption_certainty IS NULL OR interruption_certainty IN (
            'confirmed_by_device', 'technical_evidence', 'not_determined'
        )
    );

ALTER TABLE delivery_device_event
    DROP CONSTRAINT IF EXISTS ck_delivery_device_event_offline_period;
ALTER TABLE delivery_device_event
    ADD CONSTRAINT ck_delivery_device_event_offline_period CHECK (
        (offline_location_count IS NULL OR offline_location_count BETWEEN 0 AND 10000)
        AND (offline_started_at IS NULL OR offline_ended_at IS NULL OR offline_started_at <= offline_ended_at)
    );

CREATE UNIQUE INDEX IF NOT EXISTS uq_delivery_tracking_incident_alert
    ON delivery_tracking_incident(alert_id)
    WHERE alert_id IS NOT NULL;

COMMIT;
