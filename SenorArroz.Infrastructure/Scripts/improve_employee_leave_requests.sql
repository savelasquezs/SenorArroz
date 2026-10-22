-- Permisos v2. Aditiva e idempotente: ejecutar DESPUÉS de add_employee_leave_requests.sql.
-- No reclasifica personas/terminales ni reinterpreta decisiones o documentos históricos.
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '60s';
ALTER TABLE public.employee_leave_request
    ADD COLUMN IF NOT EXISTS is_all_day boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS requested_is_paid boolean,
    ADD COLUMN IF NOT EXISTS policy_version integer NOT NULL DEFAULT 1,
    ADD COLUMN IF NOT EXISTS submission_digest varchar(64);
ALTER TABLE public.employee_leave_attachment
    ADD COLUMN IF NOT EXISTS evidence_code varchar(40) NOT NULL DEFAULT 'additional',
    ADD COLUMN IF NOT EXISTS review_status varchar(16) NOT NULL DEFAULT 'submitted',
    ADD COLUMN IF NOT EXISTS review_reason varchar(1000),
    ADD COLUMN IF NOT EXISTS reviewed_by_user_id integer,
    ADD COLUMN IF NOT EXISTS reviewed_at timestamptz,
    ADD COLUMN IF NOT EXISTS replaces_attachment_id integer,
    ADD COLUMN IF NOT EXISTS superseded_at timestamptz;
DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid='public.employee_leave_request'::regclass AND conname='employee_leave_day_boundary_check') THEN
        ALTER TABLE public.employee_leave_request ADD CONSTRAINT employee_leave_day_boundary_check CHECK (
            NOT is_all_day OR ((starts_at AT TIME ZONE 'America/Bogota')::time = time '00:00'
                AND (ends_at AT TIME ZONE 'America/Bogota')::time = time '00:00'));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid='public.employee_leave_request'::regclass AND conname='employee_leave_policy_check') THEN
        ALTER TABLE public.employee_leave_request ADD CONSTRAINT employee_leave_policy_check CHECK (
            policy_version IN (1,2) AND (policy_version = 1 OR requested_is_paid IS NOT NULL)
            AND (submission_digest IS NULL OR submission_digest ~ '^[0-9A-F]{64}$'));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid='public.employee_leave_attachment'::regclass AND conname='employee_leave_file_review_check') THEN
        ALTER TABLE public.employee_leave_attachment ADD CONSTRAINT employee_leave_file_review_check CHECK (
            review_status IN ('submitted','verified','rejected') AND length(btrim(evidence_code)) > 0
            AND (review_status = 'submitted' OR (reviewed_by_user_id IS NOT NULL AND reviewed_at IS NOT NULL
                AND review_reason IS NOT NULL AND length(btrim(review_reason)) > 0)));
        ALTER TABLE public.employee_leave_attachment ADD CONSTRAINT employee_leave_file_reviewer_fk
            FOREIGN KEY (tenant_id,reviewed_by_user_id) REFERENCES public."user"(tenant_id,id);
    END IF;
END $$;
-- Existing forced RLS, permissions and tenant-aware ownership remain in place.
COMMIT;
