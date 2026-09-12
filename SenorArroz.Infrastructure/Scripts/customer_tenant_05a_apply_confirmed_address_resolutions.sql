-- Approved manual resolutions from the precheck on production.
-- Idempotent: it applies only the specified legacy AddressBranch rows.
BEGIN;

UPDATE address_branch
SET neighborhood_id = 1, delivery_fee = 3000, updated_at = now()
WHERE tenant_id = 1 AND address_id IN (297, 14568) AND branch_id = 1;

UPDATE address_branch
SET neighborhood_id = 86, delivery_fee = 3000, updated_at = now()
WHERE tenant_id = 1 AND address_id IN (13801, 13802) AND branch_id = 1;

UPDATE address_branch
SET neighborhood_id = 1, delivery_fee = 3000, updated_at = now()
WHERE tenant_id = 1 AND address_id IN (9878, 13382) AND branch_id = 1;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM address_branch
        WHERE tenant_id = 1
          AND ((address_id IN (297,14568) AND branch_id = 1 AND (neighborhood_id <> 1 OR delivery_fee <> 3000))
            OR (address_id IN (13801,13802) AND branch_id = 1 AND (neighborhood_id <> 86 OR delivery_fee <> 3000))
            OR (address_id IN (9878,13382) AND branch_id = 1 AND (neighborhood_id <> 1 OR delivery_fee <> 3000)))
    ) THEN
        RAISE EXCEPTION 'The approved AddressBranch resolutions were not applied completely.';
    END IF;
END $$;

COMMIT;
