BEGIN;
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM customer_phone WHERE active GROUP BY tenant_id,phone_normalized HAVING count(DISTINCT customer_id)>1)
 THEN RAISE EXCEPTION 'No se puede activar UNIQUE: existen teléfonos duplicados'; END IF;
 IF EXISTS(SELECT 1 FROM address WHERE normalized_address IS NOT NULL AND btrim(normalized_address)<>''
   GROUP BY tenant_id,customer_id,normalized_address HAVING count(*)>1)
 THEN RAISE EXCEPTION 'No se puede activar UNIQUE: existen direcciones físicas duplicadas'; END IF;
END $$;
DROP INDEX IF EXISTS ix_customer_phone_tenant_phone_premerge;
CREATE UNIQUE INDEX IF NOT EXISTS ux_customer_phone_tenant_phone ON customer_phone(tenant_id,phone_normalized) WHERE active;
CREATE UNIQUE INDEX IF NOT EXISTS ux_customer_phone_primary ON customer_phone(customer_id) WHERE active AND is_primary;
CREATE UNIQUE INDEX IF NOT EXISTS ux_address_branch_tenant_address_branch ON address_branch(tenant_id,address_id,branch_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_address_tenant_customer_normalized
    ON address(tenant_id,customer_id,normalized_address)
    WHERE normalized_address IS NOT NULL AND btrim(normalized_address) <> '';
COMMIT;
