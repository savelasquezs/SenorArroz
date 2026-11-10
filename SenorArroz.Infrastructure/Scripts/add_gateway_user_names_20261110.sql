SET LOCAL lock_timeout = '500ms';
SET LOCAL statement_timeout = '15000ms';
DO $gateway$
DECLARE
  reader oid;
  relation regclass := to_regclass('public."user"');
  projection text;
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
    RAISE EXCEPTION 'User table requires forced RLS';
  END IF;
  IF (SELECT count(*) FROM pg_attribute WHERE attrelid=relation AND attnum>0 AND NOT attisdropped
      AND attname IN ('id','name','tenant_id')) <> 3 OR to_regnamespace('diagnostics') IS NULL THEN
    RAISE EXCEPTION 'Audited user columns or diagnostics schema missing';
  END IF;
  IF EXISTS (SELECT 1 FROM pg_policies WHERE schemaname='public' AND tablename='user'
    AND policyname='db_reader_tenant_guard' AND (permissive<>'RESTRICTIVE' OR cmd<>'SELECT'
      OR qual<>'(tenant_id = 1)' OR roles::text<>'{senorarroz_db_reader}')) THEN
    RAISE EXCEPTION 'Conflicting reader tenant guard on user';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname='public' AND tablename='user'
    AND policyname='db_reader_tenant_guard') THEN
    CREATE POLICY db_reader_tenant_guard ON public."user" AS RESTRICTIVE
      FOR SELECT TO senorarroz_db_reader USING (tenant_id=1);
  END IF;
  REVOKE ALL PRIVILEGES ON TABLE public."user" FROM senorarroz_db_reader;
  SELECT string_agg(format('%I',attname),', ') INTO projection FROM pg_attribute
    WHERE attrelid=relation AND attnum>0 AND NOT attisdropped;
  EXECUTE format('REVOKE SELECT (%s), INSERT (%s), UPDATE (%s), REFERENCES (%s) ON public."user" FROM senorarroz_db_reader',
    projection,projection,projection,projection);
  GRANT SELECT (id,name,tenant_id) ON public."user" TO senorarroz_db_reader;
  IF has_table_privilege(reader,relation,'INSERT,UPDATE,DELETE,TRUNCATE,TRIGGER,REFERENCES')
    OR has_any_column_privilege(reader,relation,'INSERT,UPDATE,REFERENCES')
    OR EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid=relation AND attnum>0 AND NOT attisdropped
      AND attname NOT IN ('id','name','tenant_id') AND has_column_privilege(reader,relation,attname,'SELECT')) THEN
    RAISE EXCEPTION 'Unexpected user privileges remain';
  END IF;
END $gateway$;

CREATE OR REPLACE VIEW diagnostics.users
WITH (security_invoker=true, security_barrier=true) AS
SELECT id,name FROM public."user"
WHERE tenant_id=1 AND tenant_id=NULLIF(current_setting('app.current_tenant_id',true),'')::integer
  AND current_setting('app.system_scope',true) IS DISTINCT FROM 'on';
REVOKE ALL ON diagnostics.users FROM PUBLIC,senorarroz_db_reader;
GRANT USAGE ON SCHEMA diagnostics TO senorarroz_db_reader;
GRANT SELECT ON diagnostics.users TO senorarroz_db_reader;
