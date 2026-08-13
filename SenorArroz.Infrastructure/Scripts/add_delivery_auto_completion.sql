BEGIN;

ALTER TABLE branch
    ADD COLUMN IF NOT EXISTS delivery_auto_complete_enabled boolean NOT NULL DEFAULT TRUE,
    ADD COLUMN IF NOT EXISTS delivery_auto_complete_arrival_radius_meters integer NOT NULL DEFAULT 50,
    ADD COLUMN IF NOT EXISTS delivery_auto_complete_departure_radius_meters integer NOT NULL DEFAULT 120,
    ADD COLUMN IF NOT EXISTS delivery_auto_complete_min_presence_seconds integer NOT NULL DEFAULT 15;

ALTER TABLE delivery_route_stop
    ADD COLUMN IF NOT EXISTS arrival_candidate_at_utc timestamp with time zone,
    ADD COLUMN IF NOT EXISTS arrival_confirmed_at_utc timestamp with time zone,
    ADD COLUMN IF NOT EXISTS arrival_evidence_count integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS arrival_last_seen_at_utc timestamp with time zone,
    ADD COLUMN IF NOT EXISTS closest_distance_meters double precision,
    ADD COLUMN IF NOT EXISTS auto_delivered_at_utc timestamp with time zone,
    ADD COLUMN IF NOT EXISTS auto_delivery_trigger_location_id bigint,
    ADD COLUMN IF NOT EXISTS auto_delivery_departure_distance_meters double precision;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_branch_delivery_auto_complete_arrival_radius'
    ) THEN
        ALTER TABLE branch
            ADD CONSTRAINT ck_branch_delivery_auto_complete_arrival_radius
            CHECK (delivery_auto_complete_arrival_radius_meters BETWEEN 10 AND 150);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_branch_delivery_auto_complete_departure_radius'
    ) THEN
        ALTER TABLE branch
            ADD CONSTRAINT ck_branch_delivery_auto_complete_departure_radius
            CHECK (
                delivery_auto_complete_departure_radius_meters BETWEEN 20 AND 500
                AND delivery_auto_complete_departure_radius_meters > delivery_auto_complete_arrival_radius_meters
            );
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_branch_delivery_auto_complete_min_presence'
    ) THEN
        ALTER TABLE branch
            ADD CONSTRAINT ck_branch_delivery_auto_complete_min_presence
            CHECK (delivery_auto_complete_min_presence_seconds BETWEEN 5 AND 300);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_delivery_route_stop_arrival_evidence_count'
    ) THEN
        ALTER TABLE delivery_route_stop
            ADD CONSTRAINT ck_delivery_route_stop_arrival_evidence_count
            CHECK (arrival_evidence_count >= 0);
    END IF;
END $$;

COMMIT;

