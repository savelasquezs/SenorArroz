BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '60s';
ALTER TABLE public.delivery_route_proposal ADD COLUMN IF NOT EXISTS wait_deadline_at_utc timestamptz;
ALTER TABLE public.delivery_route_proposal_stop
    ADD COLUMN IF NOT EXISTS conservative_ready_at_utc timestamptz,
    ADD COLUMN IF NOT EXISTS preparation_confidence varchar(16) NOT NULL DEFAULT 'unknown',
    ADD COLUMN IF NOT EXISTS ready_within_window_probability double precision NOT NULL DEFAULT 1,
    ADD COLUMN IF NOT EXISTS preparation_sample_size integer NOT NULL DEFAULT 0;
COMMIT;
