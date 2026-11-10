SET LOCAL lock_timeout = '500ms';
SET LOCAL statement_timeout = '15000ms';
DO $gateway$
DECLARE
  entry record;
  relation regclass;
  projection text;
  column_name text;
  reader oid;
  manifest constant jsonb := '{"branch": ["id", "tenant_id", "name", "is_active", "created_at", "updated_at"], "branch_business_hour": ["id", "tenant_id", "branch_id", "day_of_week", "open_time", "close_time", "is_closed", "display_order", "created_at", "updated_at"], "commercial_profile": ["id", "tenant_id", "branch_id", "name", "description", "ingredients", "created_at", "updated_at"], "daily_promotion": ["id", "tenant_id", "branch_id", "type", "gift_product_id", "discount_percentage", "discount_scope", "minimum_order_value", "is_active", "starts_at", "ends_at", "created_at", "updated_at"], "daily_promotion_product": ["id", "tenant_id", "daily_promotion_id", "product_id", "created_at", "updated_at"], "delivery_authorized_place": ["id", "tenant_id", "branch_id", "active", "radius_meters", "created_at", "updated_at"], "delivery_device_event": ["id", "tenant_id", "event_type", "battery_level_percent", "internet_available", "gps_enabled", "recorded_at", "synced_at", "created_at", "offline_location_count"], "delivery_route": ["id", "tenant_id", "branch_id", "status", "last_assignment_at_utc", "route_started_at_utc", "planned_distance_meters", "planned_driving_duration_seconds", "stop_count", "completed_at_utc", "actual_duration_seconds", "met_sla", "created_at", "updated_at"], "delivery_route_proposal": ["id", "tenant_id", "delivery_routing_plan_id", "sequence", "status", "recommendation", "expected_departure_at_utc", "wait_seconds", "approximate_driving_duration_seconds", "approximate_distance_meters", "validated_driving_duration_seconds", "validated_distance_meters", "google_validation_status", "score", "created_at", "updated_at"], "delivery_route_proposal_stop": ["id", "tenant_id", "delivery_routing_plan_id", "delivery_route_proposal_id", "order_id", "stop_sequence", "estimated_ready_at_utc", "estimated_arrival_at_utc", "travel_from_previous_seconds", "service_seconds", "was_ready_at_generation", "is_suggested_wait", "created_at", "updated_at"], "delivery_route_stop": ["id", "tenant_id", "delivery_route_id", "order_id", "stop_sequence", "arrival_confirmed_at_utc", "auto_delivered_at_utc", "created_at", "updated_at"], "delivery_routing_plan": ["id", "tenant_id", "branch_id", "generation_number", "status", "generated_at_utc", "available_slot_count", "soon_slot_count", "solver_duration_ms", "matrix_source", "created_at", "updated_at"], "delivery_tracking_alert": ["id", "tenant_id", "branch_id", "alert_type", "severity", "status", "occurred_at", "occurrence_count", "resolved_at", "duration_seconds", "created_at", "updated_at"], "delivery_tracking_incident": ["id", "tenant_id", "branch_id", "incident_type", "started_at", "ended_at", "duration_seconds", "review_status", "final_classification", "created_at", "updated_at"], "delivery_work_session": ["id", "tenant_id", "branch_id", "app_version", "started_at", "ended_at", "status", "created_at", "updated_at"], "deliveryman_advance": ["id", "tenant_id", "branch_id", "amount", "payment_method", "created_at", "updated_at"], "deliveryman_day_state": ["id", "tenant_id", "branch_id", "date", "liquidation_mode", "blocked", "created_at", "updated_at"], "deliveryman_location": ["id", "tenant_id", "recorded_at", "accuracy_meters", "internet_available", "gps_enabled", "tracking_mode", "created_at", "synced_at"], "discount_code": ["id", "tenant_id", "branch_id", "type", "gift_product_id", "discount_percentage", "starts_at", "ends_at", "minimum_order_value", "is_active", "created_at", "updated_at"], "expense_category": ["id", "tenant_id", "name", "created_at", "updated_at"], "expense_menu_target": ["id", "tenant_id", "expense_id", "target_type", "target_id", "created_at", "updated_at"], "expense_unit_conversion": ["id", "tenant_id", "expense_id", "name", "base_quantity", "active", "created_at", "updated_at"], "inventory_balance": ["id", "tenant_id", "branch_id", "expense_id", "quantity_on_hand", "quantity_reserved", "created_at", "updated_at"], "inventory_count": ["id", "tenant_id", "branch_id", "status", "created_at", "updated_at"], "inventory_count_line": ["id", "tenant_id", "inventory_count_id", "expense_id", "expected_quantity", "counted_quantity", "difference", "created_at", "updated_at"], "inventory_movement": ["id", "tenant_id", "branch_id", "expense_id", "type", "on_hand_delta", "reserved_delta", "order_id", "transfer_id", "inventory_count_id", "created_at", "updated_at"], "inventory_transfer": ["id", "tenant_id", "source_branch_id", "destination_branch_id", "status", "dispatched_at", "received_at", "created_at", "updated_at"], "inventory_transfer_line": ["id", "tenant_id", "transfer_id", "expense_id", "quantity", "received_quantity", "created_at", "updated_at"], "kitchen_voice_event": ["id", "tenant_id", "branch_id", "order_id", "kind", "created_at", "updated_at"], "loyalty_cycle_step": ["id", "tenant_id", "branch_id", "step_index", "reward_label", "step_name", "reward_type", "gift_product_id", "discount_percentage", "is_active", "created_at", "updated_at"], "neighborhood": ["id", "tenant_id", "branch_id", "name", "delivery_fee", "active", "created_at", "updated_at"], "order": ["id", "tenant_id", "branch_id", "origin_branch_id", "type", "status", "subtotal", "total", "discount_total", "delivery_fee", "created_at", "updated_at", "reserved_for", "prepare_at", "order_source", "inventory_issue"], "order_detail": ["id", "tenant_id", "order_id", "product_id", "quantity", "unit_price", "discount", "subtotal", "created_at", "updated_at"], "product": ["id", "tenant_id", "category_id", "name", "price", "stock", "active", "weight_grams", "commercial_profile_id", "serves_people_min", "serves_people_max", "storefront_variant_label", "storefront_sort_order", "inventory_enabled", "inventory_control_mode", "created_at", "updated_at"], "product_category": ["id", "tenant_id", "branch_id", "name", "storefront_role", "created_at", "updated_at"], "product_expense_requirement": ["id", "tenant_id", "product_id", "expense_id", "base_quantity", "created_at", "updated_at"], "storefront_analytics_daily": ["id", "tenant_id", "event_date", "event_name", "branch_id", "fulfillment_type", "payment_type", "status_code", "coverage_state", "event_count", "value_sum", "created_at", "updated_at"]}'::jsonb;
BEGIN
  SELECT oid INTO reader FROM pg_roles WHERE rolname = 'senorarroz_db_reader'
    AND rolcanlogin AND NOT (rolsuper OR rolbypassrls OR rolcreaterole OR rolcreatedb OR rolreplication);
  IF reader IS NULL OR EXISTS (SELECT 1 FROM pg_auth_members WHERE member = reader)
    OR EXISTS (SELECT 1 FROM pg_class WHERE relowner = reader)
    OR EXISTS (SELECT 1 FROM pg_namespace WHERE nspowner = reader) THEN
    RAISE EXCEPTION 'Reader missing, privileged, member of another role, or object owner';
  END IF;
  IF has_schema_privilege(reader, 'public', 'CREATE') THEN
    RAISE EXCEPTION 'Reader must not have CREATE on public';
  END IF;
  FOR entry IN SELECT key, value FROM jsonb_each(manifest) LOOP
    relation := to_regclass(format('public.%I', entry.key));
    IF relation IS NULL OR NOT EXISTS (SELECT 1 FROM pg_class
      WHERE oid = relation AND relkind = 'r' AND relrowsecurity AND relforcerowsecurity) THEN
      RAISE EXCEPTION 'Missing forced RLS on %', entry.key;
    END IF;
    FOR column_name IN SELECT jsonb_array_elements_text(entry.value) LOOP
      IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = relation
        AND attname = column_name AND attnum > 0 AND NOT attisdropped) THEN
        RAISE EXCEPTION 'Missing audited column %.%', entry.key, column_name;
      END IF;
    END LOOP;
    IF EXISTS (SELECT 1 FROM pg_policies WHERE schemaname = 'public' AND tablename = entry.key
      AND policyname = 'db_reader_tenant_guard' AND (permissive <> 'RESTRICTIVE'
        OR cmd <> 'SELECT' OR qual <> '(tenant_id = 1)' OR roles::text <> '{senorarroz_db_reader}')) THEN
      RAISE EXCEPTION 'Conflicting tenant guard on %', entry.key;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname = 'public' AND tablename = entry.key
      AND policyname = 'db_reader_tenant_guard') THEN
      EXECUTE format('CREATE POLICY db_reader_tenant_guard ON %s AS RESTRICTIVE FOR SELECT TO senorarroz_db_reader USING (tenant_id = 1)', relation);
    END IF;
    EXECUTE format('REVOKE ALL PRIVILEGES ON TABLE %s FROM senorarroz_db_reader', relation);
    SELECT string_agg(format('%I', attname), ', ') INTO projection FROM pg_attribute
      WHERE attrelid = relation AND attnum > 0 AND NOT attisdropped;
    EXECUTE format('REVOKE SELECT (%s), INSERT (%s), UPDATE (%s), REFERENCES (%s) ON %s FROM senorarroz_db_reader',
      projection, projection, projection, projection, relation);
    SELECT string_agg(format('%I', v), ', ') INTO projection FROM jsonb_array_elements_text(entry.value) AS x(v);
    EXECUTE format('GRANT SELECT (%s) ON %s TO senorarroz_db_reader', projection, relation);
  END LOOP;
  FOR entry IN SELECT c.oid, c.relname FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
    WHERE n.nspname='public' AND c.relkind IN ('r','p','v','m','f') LOOP
    IF has_table_privilege(reader, entry.oid, 'INSERT,UPDATE,DELETE,TRUNCATE,TRIGGER')
      OR has_any_column_privilege(reader, entry.oid, 'INSERT,UPDATE') THEN
      RAISE EXCEPTION 'Unexpected write privilege on %', entry.relname;
    END IF;
    FOR column_name IN SELECT attname FROM pg_attribute WHERE attrelid=entry.oid AND attnum>0 AND NOT attisdropped LOOP
      IF has_column_privilege(reader, entry.oid, column_name, 'SELECT')
        AND NOT COALESCE(manifest -> entry.relname ? column_name, false) THEN
        RAISE EXCEPTION 'Unexpected reader SELECT on %.%', entry.relname, column_name;
      END IF;
    END LOOP;
  END LOOP;
  CREATE SCHEMA IF NOT EXISTS diagnostics;
  REVOKE ALL ON SCHEMA diagnostics FROM PUBLIC, senorarroz_db_reader;
  GRANT USAGE ON SCHEMA diagnostics TO senorarroz_db_reader;
  FOR entry IN SELECT key, value FROM jsonb_each(manifest) LOOP
    SELECT string_agg(format('%I', v), ', ') INTO projection FROM jsonb_array_elements_text(entry.value) AS x(v);
    EXECUTE format('CREATE OR REPLACE VIEW diagnostics.%I WITH (security_invoker=true, security_barrier=true) AS SELECT %s FROM public.%I WHERE tenant_id=1 AND tenant_id=NULLIF(current_setting(''app.current_tenant_id'', true), '''')::integer AND current_setting(''app.system_scope'', true) IS DISTINCT FROM ''on''', entry.key, projection, entry.key);
    EXECUTE format('REVOKE ALL ON diagnostics.%I FROM PUBLIC, senorarroz_db_reader', entry.key);
    EXECUTE format('GRANT SELECT ON diagnostics.%I TO senorarroz_db_reader', entry.key);
  END LOOP;
END
$gateway$;
CREATE OR REPLACE VIEW diagnostics.orders_summary
WITH (security_invoker=true, security_barrier=true) AS
SELECT branch_id, (created_at AT TIME ZONE 'America/Bogota')::date AS order_date, status,
  count(*)::bigint AS total, COALESCE(sum(total),0)::numeric AS total_cop, tenant_id
FROM public."order"
WHERE tenant_id=1 AND tenant_id=NULLIF(current_setting('app.current_tenant_id',true),'')::integer
  AND current_setting('app.system_scope',true) IS DISTINCT FROM 'on'
GROUP BY tenant_id, branch_id, (created_at AT TIME ZONE 'America/Bogota')::date, status;
REVOKE ALL ON diagnostics.orders_summary FROM PUBLIC, senorarroz_db_reader;
GRANT SELECT ON diagnostics.orders_summary TO senorarroz_db_reader;
