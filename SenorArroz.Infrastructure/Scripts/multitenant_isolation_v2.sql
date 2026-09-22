BEGIN;

DO $block$
DECLARE
    table_name text;
    constraint_row record;
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
            RAISE EXCEPTION 'Falta la tabla tenant-owned requerida: %', table_name;
        END IF;
        EXECUTE format('ALTER TABLE public.%I ADD COLUMN IF NOT EXISTS tenant_id integer', table_name);

        FOR constraint_row IN
            SELECT conname
            FROM pg_constraint
            WHERE conrelid = to_regclass('public.' || quote_ident(table_name))
              AND contype = 'c'
              AND pg_get_constraintdef(oid) ~* 'tenant_id[^)]*= *1'
        LOOP
            EXECUTE format('ALTER TABLE public.%I DROP CONSTRAINT %I', table_name, constraint_row.conname);
        END LOOP;
    END LOOP;
END $block$;

-- Durante el backfill de tenant_id se actualizan tablas con triggers de auditoría.
-- Esos cambios son estructurales, no eventos de negocio. Se suprime únicamente
-- audit_insert_log dentro de ESTA transacción para no contaminar el historial.
CREATE OR REPLACE FUNCTION public.audit_insert_log(
    p_branch_id integer,
    p_entity_type text,
    p_entity_id integer,
    p_operation_type text,
    p_changed_at timestamp with time zone,
    p_summary_text text,
    p_money_delta jsonb,
    p_before_json jsonb,
    p_after_json jsonb,
    p_metadata_json jsonb
) RETURNS void
LANGUAGE plpgsql AS $function$
DECLARE
    v_tenant_id integer;
    v_user_id integer;
    v_name text;
BEGIN
    IF current_setting('app.suppress_audit', true) = 'on' THEN
        RETURN;
    END IF;

    SELECT tenant_id
    INTO v_tenant_id
    FROM public.branch
    WHERE id = p_branch_id;

    IF v_tenant_id IS NULL THEN
        RAISE EXCEPTION 'No se pudo resolver tenant para auditoría de branch %.', p_branch_id;
    END IF;

    v_user_id := public.audit_current_user_id();
    v_name := COALESCE(
        public.audit_current_user_name(),
        (SELECT u.name FROM public."user" u WHERE u.id = v_user_id)
    );

    INSERT INTO public.entity_audit_log (
        tenant_id, branch_id, entity_type, entity_id, operation_type, business_date, changed_at,
        changed_by_user_id, changed_by_name_snapshot, summary_text, money_delta_json,
        before_json, after_json, metadata_json
    )
    VALUES (
        v_tenant_id, p_branch_id, p_entity_type, p_entity_id, p_operation_type,
        public.audit_business_date(p_changed_at), p_changed_at, v_user_id, v_name,
        p_summary_text, COALESCE(p_money_delta, '{}'::jsonb), p_before_json, p_after_json, p_metadata_json
    );
END;
$function$;

SELECT set_config ( 'app.suppress_audit', 'on', true );

CREATE OR REPLACE FUNCTION pg_temp.backfill_tenant(
    child_table text,
    child_key text,
    parent_table text,
    parent_key text DEFAULT 'id'
) RETURNS void
LANGUAGE plpgsql AS $function$
BEGIN
    EXECUTE format(
        'UPDATE public.%I child SET tenant_id = parent.tenant_id FROM public.%I parent WHERE child.%I = parent.%I AND child.tenant_id IS NULL',
        child_table, parent_table, child_key, parent_key);
END $function$;

SELECT pg_temp.backfill_tenant ( 'customer', 'branch_id', 'branch' );

SELECT pg_temp.backfill_tenant (
        'neighborhood', 'branch_id', 'branch'
    );

SELECT pg_temp.backfill_tenant ( 'order', 'branch_id', 'branch' );

SELECT pg_temp.backfill_tenant ('bank', 'branch_id', 'branch');

SELECT pg_temp.backfill_tenant (
        'branch_ai_setting', 'branch_id', 'branch'
    );

SELECT pg_temp.backfill_tenant (
        'branch_business_hour', 'branch_id', 'branch'
    );

SELECT pg_temp.backfill_tenant (
        'branch_informal_loan', 'branch_id', 'branch'
    );

SELECT pg_temp.backfill_tenant (
        'branch_print_settings', 'branch_id', 'branch'
    );

SELECT pg_temp.backfill_tenant (
        'cash_register_closure', 'branch_id', 'branch'
    );

SELECT pg_temp.backfill_tenant (
        'cash_vault_movement', 'branch_id', 'branch'
    );

SELECT pg_temp.backfill_tenant (
        'commercial_profile', 'branch_id', 'branch'
    );

SELECT pg_temp.backfill_tenant (
        'daily_audit_dispatch', 'branch_id', 'branch'
    );

SELECT pg_temp.backfill_tenant (
        'daily_promotion', 'branch_id', 'branch'
    );

SELECT pg_temp.backfill_tenant (
        'delivery_app_connection', 'branch_id', 'branch'
    );

SELECT pg_temp.backfill_tenant (
        'delivery_authorized_place', 'branch_id', 'branch'
    );

SELECT pg_temp.backfill_tenant (
        'delivery_route', 'branch_id', 'branch'
    );

SELECT pg_temp.backfill_tenant (
        'delivery_routing_plan', 'branch_id', 'branch'
    );

SELECT pg_temp.backfill_tenant (
        'delivery_tracking_alert', 'branch_id', 'branch'
    );

SELECT pg_temp.backfill_tenant (
        'delivery_tracking_incident', 'branch_id', 'branch'
    );

SELECT pg_temp.backfill_tenant (
        'delivery_work_session', 'branch_id', 'branch'
    );

SELECT pg_temp.backfill_tenant (
        'deliveryman_advance', 'branch_id', 'branch'
    );

SELECT pg_temp.backfill_tenant (
        'deliveryman_day_state', 'branch_id', 'branch'
    );

SELECT pg_temp.backfill_tenant (
        'discount_code', 'branch_id', 'branch'
    );

SELECT pg_temp.backfill_tenant (
        'entity_audit_log', 'branch_id', 'branch'
    );

DO $block$
DECLARE
    unresolved_count bigint;
    single_tenant_id integer;
BEGIN
    SELECT COUNT(*)
    INTO unresolved_count
    FROM public.entity_audit_log
    WHERE tenant_id IS NULL;

    IF unresolved_count > 0 THEN
        SELECT CASE
            WHEN COUNT(*) = 1 THEN MIN(id)
            ELSE NULL
        END
        INTO single_tenant_id
        FROM public.tenant;

        IF single_tenant_id IS NULL THEN
            RAISE EXCEPTION
                'entity_audit_log conserva % filas sin tenant_id y no existe exactamente un tenant. Se requiere resolución manual.',
                unresolved_count;
        END IF;

        UPDATE public.entity_audit_log
        SET tenant_id = single_tenant_id
        WHERE tenant_id IS NULL;

        RAISE NOTICE
            'Backfill histórico: % filas de entity_audit_log asignadas al tenant %.',
            unresolved_count,
            single_tenant_id;
    END IF;
END $block$;

DO $block$
DECLARE
    unresolved_count bigint;
BEGIN
    SELECT COUNT(*)
    INTO unresolved_count
    FROM public.entity_audit_log
    WHERE tenant_id IS NULL;

    IF unresolved_count > 0 THEN
        RAISE EXCEPTION
            'entity_audit_log conserva % filas sin tenant_id después del backfill.',
            unresolved_count;
    END IF;
END $block$;

SELECT pg_temp.backfill_tenant (
        'expense_header', 'branch_id', 'branch'
    );

SELECT pg_temp.backfill_tenant (
        'external_delivery_order', 'branch_id', 'branch'
    );

SELECT pg_temp.backfill_tenant (
        'loyalty_cycle_step', 'branch_id', 'branch'
    );

SELECT pg_temp.backfill_tenant (
        'payment_notification_outbox', 'branch_id', 'branch'
    );

SELECT pg_temp.backfill_tenant (
        'print_job', 'branch_id', 'branch'
    );

SELECT pg_temp.backfill_tenant (
        'product_category', 'branch_id', 'branch'
    );

SELECT pg_temp.backfill_tenant (
        'reservation_deposit', 'branch_id', 'branch'
    );

SELECT pg_temp.backfill_tenant (
        'storefront_checkout', 'branch_id', 'branch'
    );

SELECT pg_temp.backfill_tenant ( 'supplier', 'branch_id', 'branch' );

SELECT pg_temp.backfill_tenant (
        'whatsapp_branch_setting', 'branch_id', 'branch'
    );

SELECT pg_temp.backfill_tenant (
        'whatsapp_conversation', 'branch_id', 'branch'
    );

SELECT pg_temp.backfill_tenant (
        'whatsapp_quick_reply', 'branch_id', 'branch'
    );

SELECT pg_temp.backfill_tenant (
        'whatsapp_template', 'branch_id', 'branch'
    );

SELECT pg_temp.backfill_tenant (
        'wompi_payment_integration', 'branch_id', 'branch'
    );

-- Resolver ahora las tablas raíz que no tienen una relación propietaria suficiente.
-- Debe ocurrir ANTES de propagar tenant_id a hijos como:
--   product -> product_category
--   expense -> expense_category
--   whatsapp_commerce_session -> whatsapp_channel_setting
-- Si la base tiene más de un tenant y alguna fila raíz es ambigua, este bloque aborta.

DO $block$
DECLARE
    table_name text;
    single_tenant_id integer;
    root_tables text[] := ARRAY[
        'branch','business_document','email_outbox_message','expense_category','storefront_customer_auth_challenge',
        'supplier','tenant_ai_setting','whatsapp_channel_setting','whatsapp_template','whatsapp_webhook_event'
    ];
BEGIN
    FOREACH table_name IN ARRAY root_tables LOOP
        IF EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = to_regclass('public.' || quote_ident(table_name)) AND attname = 'tenant_id')
           AND EXISTS (SELECT 1 FROM pg_catalog.pg_class WHERE oid = to_regclass('public.' || quote_ident(table_name))) THEN
            EXECUTE format('SELECT 1 FROM public.%I WHERE tenant_id IS NULL LIMIT 1', table_name) INTO single_tenant_id;
            IF single_tenant_id IS NOT NULL THEN
                SELECT CASE WHEN count(*) = 1 THEN min(id) END INTO single_tenant_id FROM public.tenant;
                IF single_tenant_id IS NULL THEN
                    RAISE EXCEPTION 'Backfill ambiguo en %. Se requiere exactamente un tenant para filas sin relación propietaria.', table_name;
                END IF;
                EXECUTE format('UPDATE public.%I SET tenant_id = $1 WHERE tenant_id IS NULL', table_name) USING single_tenant_id;
            END IF;
            single_tenant_id := NULL;
        END IF;
    END LOOP;
END $block$;

SELECT pg_temp.backfill_tenant ('user', 'branch_id', 'branch');

SELECT pg_temp.backfill_tenant (
        'refresh_token', 'user_id', 'user'
    );

SELECT pg_temp.backfill_tenant (
        'password_reset_token', 'user_id', 'user'
    );

SELECT pg_temp.backfill_tenant (
        'user_device_token', 'user_id', 'user'
    );

SELECT pg_temp.backfill_tenant (
        'bank_transfer', 'created_by_id', 'user'
    );

SELECT pg_temp.backfill_tenant (
        'address', 'customer_id', 'customer'
    );

SELECT pg_temp.backfill_tenant (
        'customer_phone', 'customer_id', 'customer'
    );

SELECT pg_temp.backfill_tenant (
        'customer_merge_history', 'old_customer_id', 'customer'
    );

SELECT pg_temp.backfill_tenant (
        'address_branch', 'address_id', 'address'
    );

SELECT pg_temp.backfill_tenant ('app', 'bank_id', 'bank');

SELECT pg_temp.backfill_tenant (
        'app_payment', 'order_id', 'order'
    );

SELECT pg_temp.backfill_tenant (
        'bank_payment', 'order_id', 'order'
    );

SELECT pg_temp.backfill_tenant (
        'order_detail', 'order_id', 'order'
    );

SELECT pg_temp.backfill_tenant (
        'reservation_deposit', 'order_id', 'order'
    );

SELECT pg_temp.backfill_tenant (
        'payment_notification_outbox', 'order_id', 'order'
    );

SELECT pg_temp.backfill_tenant (
        'cash_closure_bank_reconciliation', 'cash_closure_id', 'cash_register_closure'
    );

SELECT pg_temp.backfill_tenant (
        'cash_closure_informal_loan', 'cash_closure_id', 'cash_register_closure'
    );

SELECT pg_temp.backfill_tenant (
        'branch_informal_loan_exempt_order', 'loan_id', 'branch_informal_loan'
    );

SELECT pg_temp.backfill_tenant (
        'product', 'category_id', 'product_category'
    );

SELECT pg_temp.backfill_tenant (
        'daily_promotion_product', 'daily_promotion_id', 'daily_promotion'
    );

SELECT pg_temp.backfill_tenant (
        'expense', 'category_id', 'expense_category'
    );

SELECT pg_temp.backfill_tenant (
        'expense_detail', 'header_id', 'expense_header'
    );

SELECT pg_temp.backfill_tenant (
        'expense_bank_payment', 'expense_header_id', 'expense_header'
    );

SELECT pg_temp.backfill_tenant (
        'expense_menu_target', 'expense_id', 'expense'
    );

SELECT pg_temp.backfill_tenant (
        'supplier_expense', 'expense_id', 'expense'
    );

SELECT pg_temp.backfill_tenant (
        'deliveryman_location', 'work_session_id', 'delivery_work_session'
    );

SELECT pg_temp.backfill_tenant (
        'deliveryman_location', 'deliveryman_id', 'user'
    );

SELECT pg_temp.backfill_tenant (
        'delivery_device_event', 'work_session_id', 'delivery_work_session'
    );

SELECT pg_temp.backfill_tenant (
        'delivery_stay', 'work_session_id', 'delivery_work_session'
    );

SELECT pg_temp.backfill_tenant (
        'delivery_tracking_incident', 'work_session_id', 'delivery_work_session'
    );

SELECT pg_temp.backfill_tenant (
        'delivery_incident_location_evidence', 'incident_id', 'delivery_tracking_incident'
    );

SELECT pg_temp.backfill_tenant (
        'delivery_incident_device_event_evidence', 'incident_id', 'delivery_tracking_incident'
    );

SELECT pg_temp.backfill_tenant (
        'delivery_route_stop', 'delivery_route_id', 'delivery_route'
    );

SELECT pg_temp.backfill_tenant (
        'delivery_route_proposal', 'delivery_routing_plan_id', 'delivery_routing_plan'
    );

SELECT pg_temp.backfill_tenant (
        'delivery_route_proposal_stop', 'delivery_routing_plan_id', 'delivery_routing_plan'
    );

SELECT pg_temp.backfill_tenant (
        'whatsapp_message', 'conversation_id', 'whatsapp_conversation'
    );

SELECT pg_temp.backfill_tenant (
        'whatsapp_ai_invocation', 'conversation_id', 'whatsapp_conversation'
    );

SELECT pg_temp.backfill_tenant (
        'whatsapp_commerce_session', 'channel_setting_id', 'whatsapp_channel_setting'
    );

SELECT pg_temp.backfill_tenant (
        'whatsapp_commerce_session_token', 'session_id', 'whatsapp_commerce_session'
    );

SELECT pg_temp.backfill_tenant (
        'whatsapp_flow_exchange', 'session_id', 'whatsapp_commerce_session'
    );

SELECT pg_temp.backfill_tenant (
        'whatsapp_commerce_event', 'session_id', 'whatsapp_commerce_session'
    );

SELECT pg_temp.backfill_tenant (
        'whatsapp_commerce_outbox', 'conversation_id', 'whatsapp_conversation'
    );

SELECT pg_temp.backfill_tenant (
        'delivery_app_store', 'connection_id', 'delivery_app_connection'
    );

SELECT pg_temp.backfill_tenant (
        'delivery_app_webhook_subscription', 'connection_id', 'delivery_app_connection'
    );

SELECT pg_temp.backfill_tenant (
        'delivery_app_product_mapping', 'connection_id', 'delivery_app_connection'
    );

SELECT pg_temp.backfill_tenant (
        'external_delivery_order', 'connection_id', 'delivery_app_connection'
    );

SELECT pg_temp.backfill_tenant (
        'integration_webhook_event', 'connection_id', 'delivery_app_connection'
    );

SELECT pg_temp.backfill_tenant (
        'rappi_menu_publication', 'connection_id', 'delivery_app_connection'
    );

SELECT pg_temp.backfill_tenant (
        'rappi_availability_state', 'connection_id', 'delivery_app_connection'
    );

SELECT pg_temp.backfill_tenant (
        'wompi_payment_attempt', 'integration_id', 'wompi_payment_integration'
    );

SELECT pg_temp.backfill_tenant (
        'wompi_provider_transaction', 'payment_attempt_id', 'wompi_payment_attempt'
    );

SELECT pg_temp.backfill_tenant (
        'wompi_webhook_event', 'integration_id', 'wompi_payment_integration'
    );

-- Terminar el período de backfill estructural. Desde aquí los eventos nuevos
-- vuelven a ser auditables normalmente.
SELECT set_config ( 'app.suppress_audit', 'off', true );

-- Reconciliación final de auditoría histórica. La primera pasada resuelve por
-- sucursal. Si quedara alguna fila legacy no resoluble y existe exactamente
-- un tenant, se asigna a ese único tenant; con múltiples tenants se aborta.
UPDATE public.entity_audit_log AS log
SET
    tenant_id = branch.tenant_id
FROM public.branch AS branch
WHERE
    log.branch_id = branch.id
    AND log.tenant_id IS NULL
    AND branch.tenant_id IS NOT NULL;

DO $block$
DECLARE
    unresolved_count bigint;
    single_tenant_id integer;
BEGIN
    SELECT COUNT(*)
    INTO unresolved_count
    FROM public.entity_audit_log
    WHERE tenant_id IS NULL;

    IF unresolved_count > 0 THEN
        SELECT CASE
            WHEN COUNT(*) = 1 THEN MIN(id)
            ELSE NULL
        END
        INTO single_tenant_id
        FROM public.tenant;

        IF single_tenant_id IS NULL THEN
            RAISE EXCEPTION
                'entity_audit_log conserva % filas sin tenant_id y no existe exactamente un tenant. Se requiere resolución manual.',
                unresolved_count;
        END IF;

        UPDATE public.entity_audit_log
        SET tenant_id = single_tenant_id
        WHERE tenant_id IS NULL;

        RAISE NOTICE
            'Backfill histórico final: % filas de entity_audit_log asignadas al tenant %.',
            unresolved_count,
            single_tenant_id;
    END IF;

    SELECT COUNT(*)
    INTO unresolved_count
    FROM public.entity_audit_log
    WHERE tenant_id IS NULL;

    IF unresolved_count > 0 THEN
        RAISE EXCEPTION
            'entity_audit_log conserva % filas sin tenant_id después de la reconciliación final.',
            unresolved_count;
    END IF;
END $block$;

CREATE OR REPLACE FUNCTION pg_temp.assert_tenant_relation(
    child_table text,
    child_key text,
    parent_table text,
    parent_key text DEFAULT 'id'
) RETURNS void
LANGUAGE plpgsql AS $function$
DECLARE
    invalid_exists boolean;
BEGIN
    EXECUTE format(
        'SELECT EXISTS (SELECT 1 FROM public.%I child LEFT JOIN public.%I parent ON child.%I = parent.%I WHERE child.%I IS NOT NULL AND (parent.%I IS NULL OR child.tenant_id IS DISTINCT FROM parent.tenant_id))',
        child_table, parent_table, child_key, parent_key, child_key, parent_key)
    INTO invalid_exists;
    IF invalid_exists THEN
        RAISE EXCEPTION 'Relación cross-tenant o huérfana: %.% -> %.%', child_table, child_key, parent_table, parent_key;
    END IF;
END $function$;

SELECT pg_temp.assert_tenant_relation (
        'customer', 'branch_id', 'branch'
    );

SELECT pg_temp.assert_tenant_relation (
        'address', 'customer_id', 'customer'
    );

SELECT pg_temp.assert_tenant_relation (
        'neighborhood', 'branch_id', 'branch'
    );

SELECT pg_temp.assert_tenant_relation (
        'order', 'branch_id', 'branch'
    );

SELECT pg_temp.assert_tenant_relation (
        'order_detail', 'order_id', 'order'
    );

SELECT pg_temp.assert_tenant_relation (
        'product', 'category_id', 'product_category'
    );

SELECT pg_temp.assert_tenant_relation (
        'daily_promotion_product', 'daily_promotion_id', 'daily_promotion'
    );

SELECT pg_temp.assert_tenant_relation (
        'expense_detail', 'header_id', 'expense_header'
    );

SELECT pg_temp.assert_tenant_relation (
        'whatsapp_message', 'conversation_id', 'whatsapp_conversation'
    );

SELECT pg_temp.assert_tenant_relation (
        'whatsapp_ai_invocation', 'conversation_id', 'whatsapp_conversation'
    );

SELECT pg_temp.assert_tenant_relation (
        'delivery_device_event', 'work_session_id', 'delivery_work_session'
    );

SELECT pg_temp.assert_tenant_relation (
        'delivery_stay', 'work_session_id', 'delivery_work_session'
    );

SELECT pg_temp.assert_tenant_relation (
        'delivery_tracking_incident', 'work_session_id', 'delivery_work_session'
    );

SELECT pg_temp.assert_tenant_relation (
        'delivery_app_store', 'connection_id', 'delivery_app_connection'
    );

SELECT pg_temp.assert_tenant_relation (
        'delivery_app_webhook_subscription', 'connection_id', 'delivery_app_connection'
    );

SELECT pg_temp.assert_tenant_relation (
        'delivery_app_product_mapping', 'connection_id', 'delivery_app_connection'
    );

SELECT pg_temp.assert_tenant_relation (
        'external_delivery_order', 'connection_id', 'delivery_app_connection'
    );

SELECT pg_temp.assert_tenant_relation (
        'integration_webhook_event', 'connection_id', 'delivery_app_connection'
    );

SELECT pg_temp.assert_tenant_relation (
        'rappi_menu_publication', 'connection_id', 'delivery_app_connection'
    );

SELECT pg_temp.assert_tenant_relation (
        'rappi_availability_state', 'connection_id', 'delivery_app_connection'
    );

SELECT pg_temp.assert_tenant_relation (
        'wompi_payment_attempt', 'integration_id', 'wompi_payment_integration'
    );

SELECT pg_temp.assert_tenant_relation (
        'wompi_provider_transaction', 'payment_attempt_id', 'wompi_payment_attempt'
    );

DO $block$
DECLARE
    table_name text;
    null_count bigint;
    index_name text;
    constraint_name text;
    tenant_tables text[] := ARRAY[
        'address','address_branch','app','app_payment','bank','bank_payment','bank_transfer','branch',
        'branch_ai_setting','branch_business_hour','branch_informal_loan','branch_informal_loan_exempt_order',
        'branch_print_settings','business_document','cash_closure_bank_reconciliation','cash_closure_informal_loan',
        'cash_register_closure','cash_vault_movement','commercial_profile','customer','customer_merge_history',
        'customer_phone','daily_audit_dispatch','daily_promotion','daily_promotion_product',
        'delivery_app_connection','delivery_app_product_mapping','delivery_app_store',
        'delivery_app_webhook_subscription','delivery_authorized_place','delivery_device_event',
        'delivery_incident_device_event_evidence','delivery_incident_location_evidence','delivery_route',
        'delivery_route_proposal','delivery_route_proposal_stop','delivery_route_stop','delivery_routing_plan',
        'delivery_stay','delivery_tracking_alert','delivery_tracking_incident','delivery_work_session',
        'deliveryman_advance','deliveryman_day_state','deliveryman_location','discount_code','email_outbox_message',
        'entity_audit_log','expense','expense_bank_payment','expense_category','expense_detail','expense_header',
        'expense_menu_target','external_delivery_order','integration_webhook_event','loyalty_cycle_step','neighborhood',
        'order','order_detail','password_reset_token','payment_notification_outbox','print_job','product',
        'product_category','rappi_availability_state','rappi_menu_publication','refresh_token','reservation_deposit',
        'storefront_checkout','storefront_customer_auth_challenge','supplier','supplier_expense','tenant_ai_setting',
        'user','user_device_token','whatsapp_ai_invocation','whatsapp_branch_setting','whatsapp_channel_setting',
        'whatsapp_commerce_event','whatsapp_commerce_outbox','whatsapp_commerce_session',
        'whatsapp_commerce_session_token','whatsapp_conversation','whatsapp_flow_exchange','whatsapp_message',
        'whatsapp_quick_reply','whatsapp_template','whatsapp_webhook_event','wompi_payment_attempt',
        'wompi_payment_integration','wompi_provider_transaction','wompi_webhook_event'
    ];
BEGIN
    FOREACH table_name IN ARRAY tenant_tables LOOP
        EXECUTE format('SELECT count(*) FROM public.%I WHERE tenant_id IS NULL', table_name) INTO null_count;
        IF null_count > 0 THEN
            RAISE EXCEPTION 'La tabla % conserva % filas sin tenant_id.', table_name, null_count;
        END IF;

        EXECUTE format('ALTER TABLE public.%I ALTER COLUMN tenant_id DROP DEFAULT', table_name);
        EXECUTE format('ALTER TABLE public.%I ALTER COLUMN tenant_id SET NOT NULL', table_name);
        index_name := 'ix_mt_' || substr(md5(table_name), 1, 16) || '_tenant';
        EXECUTE format('CREATE INDEX IF NOT EXISTS %I ON public.%I (tenant_id)', index_name, table_name);

        IF NOT EXISTS (
            SELECT 1
            FROM pg_constraint
            WHERE conrelid = to_regclass('public.' || quote_ident(table_name))
              AND contype = 'f'
              AND pg_get_constraintdef(oid) ~* 'FOREIGN KEY \(tenant_id\) REFERENCES (public\.)?tenant\(id\)'
        ) THEN
            constraint_name := 'fk_mt_' || substr(md5(table_name), 1, 16) || '_tenant';
            EXECUTE format(
                'ALTER TABLE public.%I ADD CONSTRAINT %I FOREIGN KEY (tenant_id) REFERENCES public.tenant(id) ON DELETE RESTRICT',
                table_name, constraint_name);
        END IF;
    END LOOP;
END $block$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_mt_order_tenant_id ON public."order" (tenant_id, id);

CREATE UNIQUE INDEX IF NOT EXISTS ux_mt_whatsapp_conversation_tenant_id ON public.whatsapp_conversation (tenant_id, id);

CREATE UNIQUE INDEX IF NOT EXISTS ux_mt_delivery_app_connection_tenant_id ON public.delivery_app_connection (tenant_id, id);

CREATE UNIQUE INDEX IF NOT EXISTS ux_mt_wompi_payment_attempt_tenant_id ON public.wompi_payment_attempt (tenant_id, id);

DO $block$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_mt_order_detail_tenant_order') THEN
        ALTER TABLE public.order_detail ADD CONSTRAINT fk_mt_order_detail_tenant_order
            FOREIGN KEY (tenant_id, order_id) REFERENCES public."order"(tenant_id, id) ON DELETE CASCADE;

END IF;

IF NOT EXISTS (
    SELECT 1
    FROM pg_constraint
    WHERE
        conname = 'fk_mt_whatsapp_message_tenant_conversation'
) THEN
ALTER TABLE public.whatsapp_message
ADD CONSTRAINT fk_mt_whatsapp_message_tenant_conversation FOREIGN KEY (tenant_id, conversation_id) REFERENCES public.whatsapp_conversation (tenant_id, id) ON DELETE CASCADE;

END IF;

IF NOT EXISTS (
    SELECT 1
    FROM pg_constraint
    WHERE
        conname = 'fk_mt_delivery_app_store_tenant_connection'
) THEN
ALTER TABLE public.delivery_app_store
ADD CONSTRAINT fk_mt_delivery_app_store_tenant_connection FOREIGN KEY (tenant_id, connection_id) REFERENCES public.delivery_app_connection (tenant_id, id) ON DELETE CASCADE;

END IF;

IF NOT EXISTS (
    SELECT 1
    FROM pg_constraint
    WHERE
        conname = 'fk_mt_delivery_app_webhook_tenant_connection'
) THEN
ALTER TABLE public.delivery_app_webhook_subscription
ADD CONSTRAINT fk_mt_delivery_app_webhook_tenant_connection FOREIGN KEY (tenant_id, connection_id) REFERENCES public.delivery_app_connection (tenant_id, id) ON DELETE CASCADE;

END IF;

IF NOT EXISTS (
    SELECT 1
    FROM pg_constraint
    WHERE
        conname = 'fk_mt_wompi_transaction_tenant_attempt'
) THEN
ALTER TABLE public.wompi_provider_transaction
ADD CONSTRAINT fk_mt_wompi_transaction_tenant_attempt FOREIGN KEY (tenant_id, payment_attempt_id) REFERENCES public.wompi_payment_attempt (tenant_id, id) ON DELETE CASCADE;

END IF;

END $block$;

CREATE OR REPLACE FUNCTION public.audit_insert_log(
    p_branch_id integer,
    p_entity_type text,
    p_entity_id integer,
    p_operation_type text,
    p_changed_at timestamp with time zone,
    p_summary_text text,
    p_money_delta jsonb,
    p_before_json jsonb,
    p_after_json jsonb,
    p_metadata_json jsonb
) RETURNS void
LANGUAGE plpgsql AS $function$
DECLARE
    v_tenant_id integer;
    v_user_id integer;
    v_name text;
BEGIN
    SELECT tenant_id INTO v_tenant_id FROM public.branch WHERE id = p_branch_id;
    IF v_tenant_id IS NULL THEN
        RAISE EXCEPTION 'No se pudo resolver tenant para auditoría de branch %.', p_branch_id;
    END IF;
    v_user_id := public.audit_current_user_id();
    v_name := COALESCE(public.audit_current_user_name(), (SELECT u.name FROM public."user" u WHERE u.id = v_user_id));

    INSERT INTO public.entity_audit_log (
        tenant_id, branch_id, entity_type, entity_id, operation_type, business_date, changed_at,
        changed_by_user_id, changed_by_name_snapshot, summary_text, money_delta_json,
        before_json, after_json, metadata_json)
    VALUES (
        v_tenant_id, p_branch_id, p_entity_type, p_entity_id, p_operation_type,
        public.audit_business_date(p_changed_at), p_changed_at, v_user_id, v_name,
        p_summary_text, COALESCE(p_money_delta, '{}'::jsonb), p_before_json, p_after_json, p_metadata_json);
END;
$function$;

COMMIT;