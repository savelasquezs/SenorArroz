BEGIN;

ALTER TABLE deliveryman_location
    ADD COLUMN IF NOT EXISTS client_point_id uuid,
    ADD COLUMN IF NOT EXISTS accuracy_meters double precision,
    ADD COLUMN IF NOT EXISTS heading_degrees double precision,
    ADD COLUMN IF NOT EXISTS battery_level_percent integer,
    ADD COLUMN IF NOT EXISTS internet_available boolean,
    ADD COLUMN IF NOT EXISTS gps_enabled boolean,
    ADD COLUMN IF NOT EXISTS tracking_mode varchar(30),
    ADD COLUMN IF NOT EXISTS synced_at timestamp with time zone;

CREATE UNIQUE INDEX IF NOT EXISTS uq_dloc_client_point_id
    ON deliveryman_location(client_point_id)
    WHERE client_point_id IS NOT NULL;

ALTER TABLE deliveryman_location
    DROP CONSTRAINT IF EXISTS ck_dloc_battery_level_percent;

ALTER TABLE deliveryman_location
    ADD CONSTRAINT ck_dloc_battery_level_percent
    CHECK (battery_level_percent IS NULL OR battery_level_percent BETWEEN 0 AND 100);

ALTER TABLE deliveryman_location
    DROP CONSTRAINT IF EXISTS ck_dloc_accuracy_meters;

ALTER TABLE deliveryman_location
    ADD CONSTRAINT ck_dloc_accuracy_meters
    CHECK (accuracy_meters IS NULL OR accuracy_meters >= 0);

COMMIT;
