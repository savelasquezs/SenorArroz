BEGIN;

DO $block$
DECLARE
    table_name text;
    tenant_tables text[] := ARRAY[
        'address','address_branch','app','app_payment','bank','bank_payment','bank_transfer','branch','branch_ai_setting','branch_business_hour','branch_informal_loan','branch_informal_loan_exempt_order','branch_print_settings','business_document','cash_closure_bank_reconciliation','cash_closure_informal_loan','cash_register_closure','cash_vault_movement','commercial_profile','customer','customer_merge_history','customer_phone','daily_audit_dispatch','daily_promotion','daily_promotion_product','delivery_app_connection','delivery_app_product_mapping','delivery_app_store','delivery_app_webhook_subscription','delivery_authorized_place','delivery_device_event','delivery_incident_device_event_evidence','delivery_incident_location_evidence','delivery_route','delivery_route_proposal','delivery_route_proposal_stop','delivery_route_stop','delivery_routing_plan','delivery_stay','delivery_tracking_alert','delivery_tracking_incident','delivery_work_session','deliveryman_advance','deliveryman_day_state','deliveryman_location','discount_code','email_outbox_message','entity_audit_log','expense','expense_bank_payment','expense_category','expense_detail','expense_header','expense_menu_target','external_delivery_order','integration_webhook_event','loyalty_cycle_step','neighborhood','order','order_detail','password_reset_token','payment_notification_outbox','print_job','product','product_category','rappi_availability_state','rappi_menu_publication','refresh_token','reservation_deposit','storefront_checkout','storefront_customer_auth_challenge','supplier','supplier_expense','tenant_ai_setting','user','user_device_token','whatsapp_ai_invocation','whatsapp_branch_setting','whatsapp_channel_setting','whatsapp_commerce_event','whatsapp_commerce_outbox','whatsapp_commerce_session','whatsapp_commerce_session_token','whatsapp_conversation','whatsapp_flow_exchange','whatsapp_message','whatsapp_quick_reply','whatsapp_template','whatsapp_webhook_event','wompi_payment_attempt','wompi_payment_integration','wompi_provider_transaction','wompi_webhook_event'
    ];
BEGIN
    FOREACH table_name IN ARRAY tenant_tables LOOP
        EXECUTE format('ALTER TABLE public.%I NO FORCE ROW LEVEL SECURITY', table_name);
        EXECUTE format('ALTER TABLE public.%I DISABLE ROW LEVEL SECURITY', table_name);
    END LOOP;
END $block$;

COMMIT;
