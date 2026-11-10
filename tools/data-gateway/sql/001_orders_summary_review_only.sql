-- Review manually in a disposable PostgreSQL environment before production.
-- Does not create login/role or grant access automatically.
-- Security barrier + explicit tenant scope; exposes only aggregate order metrics.
BEGIN;
CREATE SCHEMA IF NOT EXISTS diagnostics;

CREATE OR REPLACE VIEW diagnostics.orders_summary
WITH (security_barrier = true)
AS
SELECT
    o.branch_id,
    o.created_at::date AS order_date,
    o.status,
    COUNT(*)::bigint AS total,
    COALESCE(SUM(o.total), 0)::bigint AS total_cop
FROM public."order" AS o
WHERE o.tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::integer
  AND current_setting('app.system_scope', true) IS DISTINCT FROM 'on'
GROUP BY o.branch_id, o.created_at::date, o.status;

-- IMPORTANT: No GRANT here. Audit view ownership, RLS and privileges first.
-- Then manually grant USAGE on diagnostics and SELECT on this single view
-- to the dedicated reader role. Never grant CREATE on diagnostics.
COMMIT;
