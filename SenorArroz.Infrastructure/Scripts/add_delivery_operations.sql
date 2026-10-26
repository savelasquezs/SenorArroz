BEGIN;
ALTER TABLE public."user" ADD COLUMN IF NOT EXISTS delivery_context_version bigint NOT NULL DEFAULT 0;
ALTER TABLE public."user" ADD COLUMN IF NOT EXISTS last_delivery_take_id uuid;
ALTER TABLE public.delivery_route ADD COLUMN IF NOT EXISTS closure_source varchar(24);
CREATE UNIQUE INDEX IF NOT EXISTS ux_delivery_operation_receipt
 ON public.entity_audit_log(tenant_id, entity_type, entity_id, operation_type)
 WHERE entity_type = 'delivery_operation';
ALTER TABLE public."order" ADD COLUMN IF NOT EXISTS delivery_completion_source varchar(24);
CREATE OR REPLACE FUNCTION public.delivery_address_version() RETURNS trigger
LANGUAGE plpgsql SET search_path = pg_catalog,public AS $$
BEGIN NEW.updated_at := clock_timestamp(); RETURN NEW; END $$;
DROP TRIGGER IF EXISTS delivery_address_version ON public.address;
CREATE TRIGGER delivery_address_version BEFORE UPDATE ON public.address
 FOR EACH ROW EXECUTE FUNCTION public.delivery_address_version();
COMMIT;
