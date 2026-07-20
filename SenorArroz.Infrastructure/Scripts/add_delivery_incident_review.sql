BEGIN;

ALTER TABLE delivery_tracking_incident
    ADD COLUMN IF NOT EXISTS review_status varchar(50) NOT NULL DEFAULT 'pending',
    ADD COLUMN IF NOT EXISTS final_classification varchar(40),
    ADD COLUMN IF NOT EXISTS admin_notes varchar(2000),
    ADD COLUMN IF NOT EXISTS deliveryman_explanation varchar(2000),
    ADD COLUMN IF NOT EXISTS reviewed_by_user_id integer,
    ADD COLUMN IF NOT EXISTS reviewed_at timestamp with time zone;

ALTER TABLE delivery_tracking_incident
    DROP CONSTRAINT IF EXISTS ck_delivery_tracking_incident_review_status;
ALTER TABLE delivery_tracking_incident
    ADD CONSTRAINT ck_delivery_tracking_incident_review_status CHECK (review_status IN (
        'pending', 'justified', 'not_justified', 'gps_error', 'technical_failure',
        'closed_without_action', 'referred_to_disciplinary_process'
    ));

ALTER TABLE delivery_tracking_incident
    DROP CONSTRAINT IF EXISTS ck_delivery_tracking_incident_final_classification;
ALTER TABLE delivery_tracking_incident
    ADD CONSTRAINT ck_delivery_tracking_incident_final_classification CHECK (
        final_classification IS NULL OR final_classification IN (
            'branch', 'order_destination', 'authorized_place', 'traffic_or_route',
            'unexpected_place', 'gps_unreliable', 'pending_review'
        )
    );

CREATE INDEX IF NOT EXISTS idx_delivery_tracking_incident_review
    ON delivery_tracking_incident(branch_id, review_status, started_at);

COMMIT;
