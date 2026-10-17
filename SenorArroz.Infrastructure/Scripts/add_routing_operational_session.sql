-- P2: apply before code that reads the new columns. Additive; no invented historical timestamps.
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '60s';
ALTER TABLE public.delivery_route
    ADD COLUMN IF NOT EXISTS work_session_id integer,
    ADD COLUMN IF NOT EXISTS departed_at_utc timestamptz,
    ADD COLUMN IF NOT EXISTS departure_source varchar(16);
CREATE UNIQUE INDEX IF NOT EXISTS ux_delivery_work_session_route_scope
    ON public.delivery_work_session(tenant_id, id, deliveryman_id, branch_id);
CREATE INDEX IF NOT EXISTS idx_delivery_route_session_status
    ON public.delivery_route(tenant_id, work_session_id, status);
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                   WHERE conname = 'fk_delivery_route_work_session_scope'
                     AND conrelid = 'public.delivery_route'::regclass) THEN
        ALTER TABLE public.delivery_route
            ADD CONSTRAINT fk_delivery_route_work_session_scope
            FOREIGN KEY (tenant_id, work_session_id, deliveryman_id, branch_id)
            REFERENCES public.delivery_work_session(tenant_id, id, deliveryman_id, branch_id)
            ON DELETE RESTRICT;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                   WHERE conname = 'ck_delivery_route_departure_source'
                     AND conrelid = 'public.delivery_route'::regclass) THEN
        ALTER TABLE public.delivery_route ADD CONSTRAINT ck_delivery_route_departure_source
            CHECK ((departed_at_utc IS NULL AND departure_source IS NULL)
                OR (departed_at_utc IS NOT NULL AND departure_source IS NOT NULL
                    AND departure_source IN ('gps', 'confirmed')));
    END IF;
END $$;
COMMIT;
-- Application rollback keeps these nullable columns. Never drop populated history.
-- Data reconciliation is separate, tenant-scoped, previewed and audited.
