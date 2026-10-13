-- Apply BEFORE deploying the associated backend; never infer legacy provenance from an employee's current branch.
-- Run using a schema-owner/maintenance connection, not the restricted application role.
BEGIN;
ALTER TABLE "order" ADD COLUMN IF NOT EXISTS origin_branch_id integer NULL;
ALTER TABLE "order" ADD COLUMN IF NOT EXISTS management_version bigint NOT NULL DEFAULT 1;
CREATE INDEX IF NOT EXISTS ix_order_remote_origin
    ON "order" (tenant_id, origin_branch_id, branch_id, created_at DESC) WHERE origin_branch_id IS NOT NULL;
DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_order_origin_branch_tenant' AND conrelid = '"order"'::regclass) THEN
        ALTER TABLE "order" ADD CONSTRAINT fk_order_origin_branch_tenant
            FOREIGN KEY (tenant_id, origin_branch_id) REFERENCES branch(tenant_id, id) ON DELETE RESTRICT;
    END IF;
END $$;
CREATE OR REPLACE FUNCTION guard_order_management() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.origin_branch_id IS DISTINCT FROM OLD.origin_branch_id THEN
        RAISE EXCEPTION 'The originating branch is immutable' USING ERRCODE = '23514';
    END IF;
    NEW.management_version := GREATEST(NEW.management_version, OLD.management_version + 1);
    RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS trg_order_management_guard ON "order";
CREATE TRIGGER trg_order_management_guard BEFORE UPDATE ON "order"
    FOR EACH ROW EXECUTE FUNCTION guard_order_management();
COMMENT ON COLUMN "order".origin_branch_id IS 'Immutable originating workspace; NULL denotes unknown legacy provenance; branch_id owns fulfillment/accounting.';
COMMIT;
