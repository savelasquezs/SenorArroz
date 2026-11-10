-- Expansion of diagnostic reader permissions: review and apply ONLY via approved migration workflow.
-- No data mutation. No role creation, password changes, BYPASSRLS or broad schema grants.
-- Read access from MCP remains restricted to separately allowlisted diagnostics views.
BEGIN;

DO $$
DECLARE
  table_name text;
  chosen text[];
  required_table text;
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles
                 WHERE rolname = 'senorarroz_db_reader' AND NOT rolsuper
                   AND NOT rolbypassrls AND NOT rolcreaterole AND NOT rolcreatedb) THEN
    RAISE EXCEPTION 'Reader role missing or has unsafe role attributes';
  END IF;

  -- Every candidate must have forced RLS and a tenant_id column.
  FOREACH required_table IN ARRAY ARRAY[
    'branch_business_hour', 'commercial_profile', 'daily_promotion',
    'daily_promotion_product', 'expense_category', 'expense_unit_conversion',
    'loyalty_cycle_step', 'neighborhood', 'product_expense_requirement',
    'kitchen_voice_event', 'delivery_routing_plan',
    'delivery_route_proposal', 'delivery_route_proposal_stop',
    'delivery_authorized_place', 'discount_code',
    'inventory_balance', 'inventory_count', 'inventory_count_line',
    'inventory_movement', 'inventory_transfer', 'inventory_transfer_line',
    'storefront_analytics_daily'
  ]
  LOOP
    IF NOT EXISTS (
      SELECT 1
      FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
      WHERE n.nspname='public' AND c.relname=required_table
        AND c.relkind IN ('r','p') AND c.relrowsecurity AND c.relforcerowsecurity
        AND EXISTS (
          SELECT 1 FROM pg_attribute a WHERE a.attrelid=c.oid
          AND a.attname='tenant_id' AND NOT a.attisdropped
        )
    ) THEN
      RAISE EXCEPTION 'Missing forced tenant RLS on %', required_table;
    END IF;
  END LOOP;
END $$;

-- Operational configuration/catalog tables without customer credentials or PII.
GRANT SELECT ON TABLE
  public.branch_business_hour,
  public.commercial_profile,
  public.daily_promotion,
  public.daily_promotion_product,
  public.expense_category,
  public.expense_unit_conversion,
  public.loyalty_cycle_step,
  public.neighborhood,
  public.product_expense_requirement,
  public.kitchen_voice_event,
  public.delivery_routing_plan,
  public.delivery_route_proposal,
  public.delivery_route_proposal_stop,
  public.delivery_authorized_place,
  public.discount_code
TO senorarroz_db_reader;

-- Inventory: allow operational counts, not costs, notes, user attribution
-- or idempotency keys (sensitive business/diagnostic metadata).
GRANT SELECT (id,tenant_id,branch_id,expense_id,quantity_on_hand,quantity_reserved,created_at,updated_at)
  ON public.inventory_balance TO senorarroz_db_reader;
GRANT SELECT (id,tenant_id,branch_id,status,created_at,updated_at)
  ON public.inventory_count TO senorarroz_db_reader;
GRANT SELECT (id,tenant_id,inventory_count_id,expense_id,expected_quantity,counted_quantity,difference,created_at,updated_at)
  ON public.inventory_count_line TO senorarroz_db_reader;
GRANT SELECT (id,tenant_id,branch_id,expense_id,type,on_hand_delta,reserved_delta,order_id,transfer_id,inventory_count_id,created_at,updated_at)
  ON public.inventory_movement TO senorarroz_db_reader;
GRANT SELECT (id,tenant_id,source_branch_id,destination_branch_id,status,dispatched_at,received_at,created_at,updated_at)
  ON public.inventory_transfer TO senorarroz_db_reader;
GRANT SELECT (id,tenant_id,transfer_id,expense_id,quantity,received_quantity,created_at,updated_at)
  ON public.inventory_transfer_line TO senorarroz_db_reader;

-- Web analytics: aggregated counts only, excluding query-like UTM fields.
GRANT SELECT (id,tenant_id,event_date,event_name,branch_id,fulfillment_type,payment_type,status_code,coverage_state,event_count,value_sum,created_at,updated_at)
  ON public.storefront_analytics_daily TO senorarroz_db_reader;

-- No new grants on user, customer, address, tokens, phone, payment,
-- electronic invoice, Rappi/WhatsApp webhook, integrations or backup tables.
-- Never GRANT SELECT ON ALL TABLES or ALTER DEFAULT PRIVILEGES here.
COMMIT;
