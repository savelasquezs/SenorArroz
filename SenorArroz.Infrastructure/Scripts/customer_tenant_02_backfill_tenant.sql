BEGIN;

UPDATE branch SET tenant_id = 1 WHERE tenant_id IS NULL;
UPDATE customer c SET tenant_id = b.tenant_id FROM branch b WHERE c.branch_id = b.id AND c.tenant_id IS NULL;
UPDATE neighborhood n SET tenant_id = b.tenant_id FROM branch b WHERE n.branch_id = b.id AND n.tenant_id IS NULL;
UPDATE address a SET tenant_id = c.tenant_id FROM customer c WHERE a.customer_id = c.id AND a.tenant_id IS NULL;
UPDATE "order" o SET tenant_id = b.tenant_id FROM branch b WHERE o.branch_id = b.id AND o.tenant_id IS NULL;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM branch WHERE tenant_id IS NULL)
       OR EXISTS (SELECT 1 FROM customer WHERE tenant_id IS NULL)
       OR EXISTS (SELECT 1 FROM address WHERE tenant_id IS NULL)
       OR EXISTS (SELECT 1 FROM neighborhood WHERE tenant_id IS NULL)
       OR EXISTS (SELECT 1 FROM "order" WHERE tenant_id IS NULL) THEN
        RAISE EXCEPTION 'Tenant backfill incompleto; transacción cancelada';
    END IF;
END $$;

ALTER TABLE branch ALTER COLUMN tenant_id SET NOT NULL;
ALTER TABLE customer ALTER COLUMN tenant_id SET NOT NULL;
ALTER TABLE address ALTER COLUMN tenant_id SET NOT NULL;
ALTER TABLE neighborhood ALTER COLUMN tenant_id SET NOT NULL;
ALTER TABLE "order" ALTER COLUMN tenant_id SET NOT NULL;
ALTER TABLE branch ALTER COLUMN tenant_id SET DEFAULT 1;
ALTER TABLE customer ALTER COLUMN tenant_id SET DEFAULT 1;
ALTER TABLE address ALTER COLUMN tenant_id SET DEFAULT 1;
ALTER TABLE neighborhood ALTER COLUMN tenant_id SET DEFAULT 1;
ALTER TABLE "order" ALTER COLUMN tenant_id SET DEFAULT 1;

DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_constraint WHERE conname='fk_branch_tenant') THEN ALTER TABLE branch ADD CONSTRAINT fk_branch_tenant FOREIGN KEY(tenant_id) REFERENCES tenant(id) ON DELETE RESTRICT; END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_constraint WHERE conname='fk_customer_tenant') THEN ALTER TABLE customer ADD CONSTRAINT fk_customer_tenant FOREIGN KEY(tenant_id) REFERENCES tenant(id) ON DELETE RESTRICT; END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_constraint WHERE conname='fk_address_tenant') THEN ALTER TABLE address ADD CONSTRAINT fk_address_tenant FOREIGN KEY(tenant_id) REFERENCES tenant(id) ON DELETE RESTRICT; END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_constraint WHERE conname='fk_neighborhood_tenant') THEN ALTER TABLE neighborhood ADD CONSTRAINT fk_neighborhood_tenant FOREIGN KEY(tenant_id) REFERENCES tenant(id) ON DELETE RESTRICT; END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_constraint WHERE conname='fk_order_tenant') THEN ALTER TABLE "order" ADD CONSTRAINT fk_order_tenant FOREIGN KEY(tenant_id) REFERENCES tenant(id) ON DELETE RESTRICT; END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_branch_tenant ON branch(tenant_id);
CREATE INDEX IF NOT EXISTS ix_customer_tenant ON customer(tenant_id);
CREATE INDEX IF NOT EXISTS ix_address_tenant_customer ON address(tenant_id, customer_id);
CREATE INDEX IF NOT EXISTS ix_neighborhood_tenant_branch ON neighborhood(tenant_id, branch_id);
CREATE INDEX IF NOT EXISTS ix_order_tenant_customer_branch ON "order"(tenant_id, customer_id, branch_id);

COMMIT;
