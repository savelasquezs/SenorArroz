BEGIN;

WITH source AS (
    SELECT c.tenant_id, c.id customer_id, v.ordinality = 1 is_primary,
           CASE WHEN length(regexp_replace(v.phone, '[^0-9]', '', 'g')) = 12
                     AND regexp_replace(v.phone, '[^0-9]', '', 'g') LIKE '57%'
                THEN substring(regexp_replace(v.phone, '[^0-9]', '', 'g') FROM 3)
                ELSE regexp_replace(v.phone, '[^0-9]', '', 'g') END phone_normalized
    FROM customer c
    CROSS JOIN LATERAL unnest(ARRAY[c.phone1, c.phone2]) WITH ORDINALITY v(phone, ordinality)
    WHERE nullif(trim(v.phone), '') IS NOT NULL
)
INSERT INTO customer_phone(tenant_id, customer_id, phone_normalized, is_primary, active)
SELECT tenant_id, customer_id, phone_normalized, bool_or(is_primary), true
FROM source
WHERE phone_normalized ~ '^3[0-9]{9}$'
GROUP BY tenant_id, customer_id, phone_normalized
ON CONFLICT (customer_id, phone_normalized) DO UPDATE
SET active = true,
    is_primary = customer_phone.is_primary OR excluded.is_primary,
    updated_at = now();

WITH ranked AS (
    SELECT id, row_number() OVER(PARTITION BY customer_id ORDER BY is_primary DESC,created_at,id) rn
    FROM customer_phone WHERE active
)
UPDATE customer_phone p SET is_primary = ranked.rn = 1, updated_at = now()
FROM ranked WHERE p.id = ranked.id AND p.is_primary IS DISTINCT FROM (ranked.rn = 1);

COMMIT;
