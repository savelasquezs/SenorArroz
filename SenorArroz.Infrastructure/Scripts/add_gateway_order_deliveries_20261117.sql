SET LOCAL lock_timeout = '500ms';
SET LOCAL statement_timeout = '15000ms';
DO $gateway$
DECLARE
  reader oid;
  relation regclass := to_regclass('public."order"');
  allowed_columns constant text[] := ARRAY[
    'id','tenant_id','branch_id','origin_branch_id','type','status','subtotal','total',
    'discount_total','delivery_fee','created_at','updated_at','reserved_for','prepare_at',
    'order_source','inventory_issue','delivery_man_id','status_times'];
BEGIN
  SELECT oid INTO reader FROM pg_roles WHERE rolname='senorarroz_db_reader'
    AND rolcanlogin AND NOT (rolsuper OR rolbypassrls OR rolcreaterole OR rolcreatedb OR rolreplication);
  IF reader IS NULL OR EXISTS (SELECT 1 FROM pg_auth_members WHERE member=reader)
    OR EXISTS (SELECT 1 FROM pg_class WHERE relowner=reader)
    OR EXISTS (SELECT 1 FROM pg_namespace WHERE nspowner=reader)
    OR has_schema_privilege(reader,'public','CREATE') THEN
    RAISE EXCEPTION 'Reader missing or unsafe';
  END IF;
  IF relation IS NULL OR NOT EXISTS (SELECT 1 FROM pg_class
    WHERE oid=relation AND relkind='r' AND relrowsecurity AND relforcerowsecurity) THEN
    RAISE EXCEPTION 'Order table requires forced RLS';
  END IF;
  IF EXISTS (SELECT 1 FROM unnest(allowed_columns) AS x(name)
    WHERE NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid=relation
      AND attnum>0 AND NOT attisdropped AND attname=x.name))
    OR NOT EXISTS (SELECT 1 FROM pg_class WHERE oid=to_regclass('diagnostics."order"')
      AND relkind='v' AND reloptions @> ARRAY['security_invoker=true','security_barrier=true']) THEN
    RAISE EXCEPTION 'Audited order columns or diagnostics view missing';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname='public' AND tablename='order'
    AND policyname='db_reader_tenant_guard' AND permissive='RESTRICTIVE' AND cmd='SELECT'
    AND qual='(tenant_id = 1)' AND roles::text='{senorarroz_db_reader}') THEN
    RAISE EXCEPTION 'Order reader tenant guard missing or unsafe';
  END IF;
  GRANT SELECT (delivery_man_id,status_times) ON public."order" TO senorarroz_db_reader;
  IF has_table_privilege(reader,relation,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,TRIGGER,REFERENCES')
    OR has_any_column_privilege(reader,relation,'INSERT,UPDATE,REFERENCES')
    OR EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid=relation AND attnum>0 AND NOT attisdropped
      AND NOT (attname=ANY(allowed_columns)) AND has_column_privilege(reader,relation,attname,'SELECT')) THEN
    RAISE EXCEPTION 'Unexpected order privileges remain';
  END IF;
END $gateway$;

CREATE OR REPLACE VIEW diagnostics."order"
WITH (security_invoker=true, security_barrier=true) AS
SELECT id,tenant_id,branch_id,origin_branch_id,type,status,subtotal,total,discount_total,
  delivery_fee,created_at,updated_at,reserved_for,prepare_at,order_source,inventory_issue,
  delivery_man_id,
  CASE WHEN status='delivered'
    AND (status_times->>'delivered') ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}[Tt ][0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]+)?([Zz]|[+-][0-9]{2}:[0-9]{2})$'
    AND pg_input_is_valid(status_times->>'delivered','timestamp with time zone')
    THEN (status_times->>'delivered')::timestamptz
    ELSE NULL
  END AS delivered_at
FROM public."order"
WHERE tenant_id=1 AND tenant_id=NULLIF(current_setting('app.current_tenant_id',true),'')::integer
  AND current_setting('app.system_scope',true) IS DISTINCT FROM 'on';
REVOKE ALL ON diagnostics."order" FROM PUBLIC,senorarroz_db_reader;
GRANT SELECT ON diagnostics."order" TO senorarroz_db_reader;
