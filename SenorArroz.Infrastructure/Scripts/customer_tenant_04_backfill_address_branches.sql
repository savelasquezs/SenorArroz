BEGIN;

UPDATE address
SET normalized_address = customer_address_normalize(coalesce(nullif(original_address,''),address))
WHERE normalized_address IS DISTINCT FROM customer_address_normalize(coalesce(nullif(original_address,''),address));

INSERT INTO address_branch(tenant_id, address_id, branch_id, neighborhood_id, delivery_fee, is_covered, validated_at, last_used_at)
SELECT a.tenant_id, a.id, c.branch_id, a.neighborhood_id, a.delivery_fee, true, a.validated_at,
       (SELECT max(o.created_at) FROM "order" o WHERE o.address_id = a.id)
FROM address a
JOIN customer c ON c.id = a.customer_id AND c.tenant_id = a.tenant_id
ON CONFLICT (tenant_id, address_id, branch_id) DO NOTHING;

COMMIT;
