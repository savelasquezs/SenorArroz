SET LOCAL lock_timeout = '500ms';
SET LOCAL statement_timeout = '15000ms';
DO $gateway$
DECLARE
  reader oid;
  entry record;
  relation regclass;
  projection text;
  all_columns text;
  manifest constant jsonb := '{"employee_lunch":["id","tenant_id","branch_id","user_id","started_at","ended_at","updated_at"],"delivery_tracking_alert":["id","tenant_id","branch_id","alert_type","severity","status","occurred_at","occurrence_count","resolved_at","duration_seconds","created_at","updated_at","deliveryman_id","incident_id"],"delivery_tracking_incident":["id","tenant_id","branch_id","incident_type","started_at","ended_at","duration_seconds","review_status","final_classification","created_at","updated_at","deliveryman_id","evidence_complete"]}'::jsonb;
BEGIN
  SELECT oid INTO reader FROM pg_roles WHERE rolname='senorarroz_db_reader'
    AND rolcanlogin AND NOT (rolsuper OR rolbypassrls OR rolcreaterole OR rolcreatedb OR rolreplication);
  IF reader IS NULL OR EXISTS (SELECT 1 FROM pg_auth_members WHERE member=reader)
    OR EXISTS (SELECT 1 FROM pg_class WHERE relowner=reader)
    OR EXISTS (SELECT 1 FROM pg_namespace WHERE nspowner=reader)
    OR has_schema_privilege(reader,'public','CREATE') OR to_regnamespace('diagnostics') IS NULL THEN
    RAISE EXCEPTION 'Reader or diagnostics schema missing or unsafe';
  END IF;
  FOR entry IN SELECT key,value FROM jsonb_each(manifest) LOOP
    relation := to_regclass(format('public.%I',entry.key));
    IF relation IS NULL OR NOT EXISTS (SELECT 1 FROM pg_class
      WHERE oid=relation AND relkind='r' AND relrowsecurity AND relforcerowsecurity) THEN
      RAISE EXCEPTION 'Missing forced RLS on %',entry.key;
    END IF;
    IF EXISTS (SELECT 1 FROM jsonb_array_elements_text(entry.value) AS x(name)
      WHERE NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid=relation
        AND attnum>0 AND NOT attisdropped AND attname=x.name)) THEN
      RAISE EXCEPTION 'Missing audited column on %',entry.key;
    END IF;
    IF EXISTS (SELECT 1 FROM pg_policies WHERE schemaname='public' AND tablename=entry.key
      AND policyname='db_reader_tenant_guard' AND (permissive<>'RESTRICTIVE' OR cmd<>'SELECT'
        OR qual<>'(tenant_id = 1)' OR roles::text<>'{senorarroz_db_reader}')) THEN
      RAISE EXCEPTION 'Conflicting reader tenant guard on %',entry.key;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname='public' AND tablename=entry.key
      AND policyname='db_reader_tenant_guard') THEN
      EXECUTE format('CREATE POLICY db_reader_tenant_guard ON %s AS RESTRICTIVE FOR SELECT TO senorarroz_db_reader USING (tenant_id=1)',relation);
    END IF;
    EXECUTE format('REVOKE ALL PRIVILEGES ON TABLE %s FROM senorarroz_db_reader',relation);
    SELECT string_agg(format('%I',attname),', ') INTO all_columns FROM pg_attribute
      WHERE attrelid=relation AND attnum>0 AND NOT attisdropped;
    EXECUTE format('REVOKE SELECT (%s), INSERT (%s), UPDATE (%s), REFERENCES (%s) ON %s FROM senorarroz_db_reader',
      all_columns,all_columns,all_columns,all_columns,relation);
    SELECT string_agg(format('%I',name),', ' ORDER BY position) INTO projection
      FROM jsonb_array_elements_text(entry.value) WITH ORDINALITY AS x(name,position);
    EXECUTE format('GRANT SELECT (%s) ON %s TO senorarroz_db_reader',projection,relation);
    IF has_table_privilege(reader,relation,'INSERT,UPDATE,DELETE,TRUNCATE,TRIGGER,REFERENCES')
      OR has_any_column_privilege(reader,relation,'INSERT,UPDATE,REFERENCES')
      OR EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid=relation AND attnum>0 AND NOT attisdropped
        AND NOT (entry.value ? attname::text) AND has_column_privilege(reader,relation,attname,'SELECT')) THEN
      RAISE EXCEPTION 'Unexpected reader privileges remain on %',entry.key;
    END IF;
    EXECUTE format('CREATE OR REPLACE VIEW diagnostics.%I WITH (security_invoker=true, security_barrier=true) AS SELECT %s FROM %s WHERE tenant_id=1 AND tenant_id=NULLIF(current_setting(''app.current_tenant_id'',true),'''')::integer AND current_setting(''app.system_scope'',true) IS DISTINCT FROM ''on''',entry.key,projection,relation);
    EXECUTE format('REVOKE ALL ON diagnostics.%I FROM PUBLIC,senorarroz_db_reader',entry.key);
    EXECUTE format('GRANT SELECT ON diagnostics.%I TO senorarroz_db_reader',entry.key);
  END LOOP;
END $gateway$;
