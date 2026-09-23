BEGIN;

CREATE SCHEMA IF NOT EXISTS app;

CREATE OR REPLACE FUNCTION app.current_tenant_id()
RETURNS integer
LANGUAGE plpgsql
STABLE
AS $function$
DECLARE
    value text;
BEGIN
    value := current_setting('app.current_tenant_id', true);
    IF value IS NULL OR value !~ '^[1-9][0-9]*$' THEN
        RETURN NULL;
    END IF;
    RETURN value::integer;
END;
$function$;

CREATE OR REPLACE FUNCTION app.is_system_scope()
RETURNS boolean
LANGUAGE sql
STABLE
AS $function$
    SELECT COALESCE(current_setting('app.system_scope', true), 'off') = 'on'
$function$;

REVOKE ALL ON SCHEMA app FROM PUBLIC;
GRANT USAGE ON SCHEMA app TO PUBLIC;
REVOKE ALL ON FUNCTION app.current_tenant_id() FROM PUBLIC;
REVOKE ALL ON FUNCTION app.is_system_scope() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION app.current_tenant_id() TO PUBLIC;
GRANT EXECUTE ON FUNCTION app.is_system_scope() TO PUBLIC;

DO $block$
DECLARE
    table_name text;
    policy_name text;
    tenant_tables text[] := ARRAY[
        'address','address_branch','app','app_payment','bank','bank_payment','bank_transfer',
        'branch','branch_ai_setting','branch_business_hour','branch_informal_loan',
        'branch_informal_loan_exempt_order','branch_print_settings','business_document',
        'cash_closure_bank_reconciliation','cash_closure_informal_loan','cash_register_closure',
        'cash_vault_movement','commercial_profile','customer','customer_merge_history','customer_phone',
        'daily_audit_dispatch','daily_promotion','daily_promotion_product','delivery_app_connection',
        'delivery_app_product_mapping','delivery_app_store','delivery_app_webhook_subscription',
        'delivery_authorized_place','delivery_device_event','delivery_incident_device_event_evidence',
        'delivery_incident_location_evidence','delivery_route','delivery_route_proposal',
        'delivery_route_proposal_stop','delivery_route_stop','delivery_routing_plan','delivery_stay',
        'delivery_tracking_alert','delivery_tracking_incident','delivery_work_session','deliveryman_advance',
        'deliveryman_day_state','deliveryman_location','discount_code','email_outbox_message',
        'entity_audit_log','expense','expense_bank_payment','expense_category','expense_detail',
        'expense_header','expense_menu_target','external_delivery_order','integration_webhook_event',
        'loyalty_cycle_step','neighborhood','order','order_detail','password_reset_token',
        'payment_notification_outbox','print_job','product','product_category','rappi_availability_state',
        'rappi_menu_publication','refresh_token','reservation_deposit','storefront_checkout',
        'storefront_customer_auth_challenge','supplier','supplier_expense','tenant_ai_setting','user',
        'user_device_token','whatsapp_ai_invocation','whatsapp_branch_setting','whatsapp_channel_setting',
        'whatsapp_commerce_event','whatsapp_commerce_outbox','whatsapp_commerce_session',
        'whatsapp_commerce_session_token','whatsapp_conversation','whatsapp_flow_exchange',
        'whatsapp_message','whatsapp_quick_reply','whatsapp_template','whatsapp_webhook_event',
        'wompi_payment_attempt','wompi_payment_integration','wompi_provider_transaction','wompi_webhook_event'
    ];
BEGIN
    FOREACH table_name IN ARRAY tenant_tables LOOP
        IF to_regclass('public.' || quote_ident(table_name)) IS NULL THEN
            RAISE EXCEPTION 'Falta la tabla tenant-owned requerida para RLS: %', table_name;
        END IF;
        IF NOT EXISTS (
            SELECT 1
            FROM pg_attribute
            WHERE attrelid = to_regclass('public.' || quote_ident(table_name))
              AND attname = 'tenant_id'
              AND attnotnull
              AND NOT attisdropped
        ) THEN
            RAISE EXCEPTION 'La tabla % no tiene tenant_id NOT NULL.', table_name;
        END IF;

        EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY', table_name);
        EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY', table_name);

        FOREACH policy_name IN ARRAY ARRAY[
            'tenant_select_policy',
            'tenant_insert_policy',
            'tenant_update_policy',
            'tenant_delete_policy'
        ] LOOP
            EXECUTE format('DROP POLICY IF EXISTS %I ON public.%I', policy_name, table_name);
        END LOOP;

        EXECUTE format(
            'CREATE POLICY tenant_select_policy ON public.%I FOR SELECT USING (app.is_system_scope() OR tenant_id = app.current_tenant_id())',
            table_name);
        EXECUTE format(
            'CREATE POLICY tenant_insert_policy ON public.%I FOR INSERT WITH CHECK (app.is_system_scope() OR tenant_id = app.current_tenant_id())',
            table_name);
        EXECUTE format(
            'CREATE POLICY tenant_update_policy ON public.%I FOR UPDATE USING (app.is_system_scope() OR tenant_id = app.current_tenant_id()) WITH CHECK (app.is_system_scope() OR tenant_id = app.current_tenant_id())',
            table_name);
        EXECUTE format(
            'CREATE POLICY tenant_delete_policy ON public.%I FOR DELETE USING (app.is_system_scope() OR tenant_id = app.current_tenant_id())',
            table_name);
    END LOOP;

    IF to_regclass('public.blog_post') IS NOT NULL THEN
        ALTER TABLE public.blog_post ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.blog_post FORCE ROW LEVEL SECURITY;
        DROP POLICY IF EXISTS tenant_select_policy ON public.blog_post;
        DROP POLICY IF EXISTS tenant_insert_policy ON public.blog_post;
        DROP POLICY IF EXISTS tenant_update_policy ON public.blog_post;
        DROP POLICY IF EXISTS tenant_delete_policy ON public.blog_post;
        CREATE POLICY tenant_select_policy ON public.blog_post FOR SELECT
            USING (app.is_system_scope() OR tenant_id = app.current_tenant_id());
        CREATE POLICY tenant_insert_policy ON public.blog_post FOR INSERT
            WITH CHECK (app.is_system_scope() OR tenant_id = app.current_tenant_id());
        CREATE POLICY tenant_update_policy ON public.blog_post FOR UPDATE
            USING (app.is_system_scope() OR tenant_id = app.current_tenant_id())
            WITH CHECK (app.is_system_scope() OR tenant_id = app.current_tenant_id());
        CREATE POLICY tenant_delete_policy ON public.blog_post FOR DELETE
            USING (app.is_system_scope() OR tenant_id = app.current_tenant_id());
    END IF;
END $block$;

COMMIT;
