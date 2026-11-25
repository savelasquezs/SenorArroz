BEGIN;
SET LOCAL lock_timeout = '5s';

CREATE TABLE IF NOT EXISTS public.nominarte_feed_head (
    tenant_id integer PRIMARY KEY REFERENCES public.tenant (id),
    position bigint NOT NULL CHECK (position >= 0)
);

CREATE TABLE IF NOT EXISTS public.nominarte_feed_entry (
    tenant_id integer NOT NULL REFERENCES public.tenant (id),
    position bigint NOT NULL,
    source text NOT NULL CHECK (source IN ('lunch', 'leave')),
    source_id integer NOT NULL,
    operation text NOT NULL CHECK (operation IN ('upsert', 'delete')),
    occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    payload jsonb NOT NULL,
    PRIMARY KEY (tenant_id, position)
);

ALTER TABLE public.nominarte_feed_head ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.nominarte_feed_head FORCE ROW LEVEL SECURITY;
ALTER TABLE public.nominarte_feed_entry ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.nominarte_feed_entry FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS nominarte_feed_tenant ON public.nominarte_feed_head;
CREATE POLICY nominarte_feed_tenant ON public.nominarte_feed_head
    USING (tenant_id = app.current_tenant_id() OR app.is_system_scope())
    WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_system_scope());

DROP POLICY IF EXISTS nominarte_feed_tenant ON public.nominarte_feed_entry;
CREATE POLICY nominarte_feed_tenant ON public.nominarte_feed_entry
    USING (tenant_id = app.current_tenant_id() OR app.is_system_scope())
    WITH CHECK (tenant_id = app.current_tenant_id() OR app.is_system_scope());

CREATE OR REPLACE FUNCTION public.nominarte_source_payload(source text, doc jsonb)
RETURNS jsonb LANGUAGE sql IMMUTABLE SET search_path = pg_catalog, public AS $$
    SELECT jsonb_build_object(
        'employeeExternalId', doc->'user_id',
        'branchExternalId', doc->'branch_id',
        'startsAt', CASE source WHEN 'lunch' THEN doc->'started_at' ELSE doc->'starts_at' END,
        'endsAt', CASE source WHEN 'lunch' THEN doc->'ended_at' ELSE doc->'ends_at' END
    ) || CASE source WHEN 'leave' THEN jsonb_build_object(
        'kind', doc->'kind',
        'status', doc->'status',
        'isPaid', doc->'is_paid',
        'isAllDay', doc->'is_all_day',
        'supportStatus', doc->'support_status',
        'version', doc->'version'
    ) ELSE '{}'::jsonb END
$$;

CREATE OR REPLACE FUNCTION public.append_nominarte_change() RETURNS trigger
LANGUAGE plpgsql SET search_path = pg_catalog, public AS $$
DECLARE
    current_doc jsonb;
    current_payload jsonb;
    next_position bigint;
    affected_tenant integer;
BEGIN
    current_doc := CASE WHEN TG_OP = 'DELETE' THEN to_jsonb(OLD) ELSE to_jsonb(NEW) END;
    current_payload := public.nominarte_source_payload(TG_ARGV[0], current_doc);
    IF TG_OP = 'UPDATE' AND current_payload = public.nominarte_source_payload(TG_ARGV[0], to_jsonb(OLD)) THEN
        RETURN NULL;
    END IF;
    affected_tenant := (current_doc->>'tenant_id')::integer;
    INSERT INTO public.nominarte_feed_head AS head(tenant_id, position)
        VALUES (affected_tenant, 1)
        ON CONFLICT (tenant_id) DO UPDATE SET position = head.position + 1
        RETURNING position INTO next_position;

    INSERT INTO public.nominarte_feed_entry (tenant_id, position, source, source_id, operation, payload)
        VALUES (affected_tenant, next_position, TG_ARGV[0], (current_doc->>'id')::integer,
            CASE WHEN TG_OP = 'DELETE' THEN 'delete' ELSE 'upsert' END, current_payload);
    RETURN NULL;
END;
$$;

DROP TRIGGER IF EXISTS publish_nominarte_change ON public.employee_lunch;
CREATE TRIGGER publish_nominarte_change AFTER INSERT OR UPDATE OR DELETE ON public.employee_lunch
    FOR EACH ROW EXECUTE FUNCTION public.append_nominarte_change('lunch');

DROP TRIGGER IF EXISTS publish_nominarte_change ON public.employee_leave_request;
CREATE TRIGGER publish_nominarte_change AFTER INSERT OR UPDATE OR DELETE ON public.employee_leave_request
    FOR EACH ROW EXECUTE FUNCTION public.append_nominarte_change('leave');

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'senorarroz_runtime') THEN
        GRANT SELECT, INSERT, UPDATE ON public.nominarte_feed_head TO senorarroz_runtime;
        GRANT SELECT, INSERT ON public.nominarte_feed_entry TO senorarroz_runtime;
    END IF;
END
$$;

COMMIT;
