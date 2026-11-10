# Auditoría de permisos del lector

Inventario de 129 tablas: Issues privados #79–#82; columnas históricas: #85 (2026-11-10).

38 tablas con SELECT por columna y 39 vistas security_invoker/security_barrier. Las 14 concesiones históricas se sustituyen; nunca se amplían con SELECT de tabla completa. Las otras 91 tablas permanecen excluidas. Tenant fijo: 1.

La migración es transaccional, idempotente y falla ante RLS ausente, columnas distintas, privilegios heredados, escritura o SELECT fuera de la lista. No crea roles ni modifica contraseñas. Las vistas incluyen el filtro tenant=1 además de RLS; el propietario no se usa para acceder a las tablas base.

| Tabla | RLS forzado | Decisión |
|---|---|---|
| `__EFMigrationsHistory` | No | Excluida: sin aislamiento tenant forzado |
| `address` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `address_branch` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `app` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `app_payment` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `bank` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `bank_payment` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `bank_transfer` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `billing_recipient_profile` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `blog_post` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `branch` | Sí | SELECT por columna; vista diagnostics.branch |
| `branch_ai_setting` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `branch_business_hour` | Sí | SELECT por columna; vista diagnostics.branch_business_hour |
| `branch_informal_loan` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `branch_informal_loan_exempt_order` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `branch_informal_loan_payment` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `branch_print_settings` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `business_document` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `cash_closure_bank_reconciliation` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `cash_closure_informal_loan` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `cash_register_closure` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `cash_vault_movement` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `commercial_profile` | Sí | SELECT por columna; vista diagnostics.commercial_profile |
| `customer` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `customer_billing_recipient` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `customer_merge_conflict` | No | Excluida: sin aislamiento tenant forzado |
| `customer_merge_history` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `customer_merge_run` | No | Excluida: sin aislamiento tenant forzado |
| `customer_phone` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `daily_audit_dispatch` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `daily_promotion` | Sí | SELECT por columna; vista diagnostics.daily_promotion |
| `daily_promotion_product` | Sí | SELECT por columna; vista diagnostics.daily_promotion_product |
| `db_assistant_migrations` | No | Excluida: sin aislamiento tenant forzado |
| `delivery_app_connection` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `delivery_app_product_mapping` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `delivery_app_store` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `delivery_app_webhook_subscription` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `delivery_authorized_place` | Sí | SELECT por columna; vista diagnostics.delivery_authorized_place |
| `delivery_device_event` | Sí | SELECT por columna; vista diagnostics.delivery_device_event |
| `delivery_incident_device_event_evidence` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `delivery_incident_location_evidence` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `delivery_route` | Sí | SELECT por columna; vista diagnostics.delivery_route |
| `delivery_route_proposal` | Sí | SELECT por columna; vista diagnostics.delivery_route_proposal |
| `delivery_route_proposal_stop` | Sí | SELECT por columna; vista diagnostics.delivery_route_proposal_stop |
| `delivery_route_stop` | Sí | SELECT por columna; vista diagnostics.delivery_route_stop |
| `delivery_routing_plan` | Sí | SELECT por columna; vista diagnostics.delivery_routing_plan |
| `delivery_stay` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `delivery_tracking_alert` | Sí | SELECT por columna; vista diagnostics.delivery_tracking_alert |
| `delivery_tracking_incident` | Sí | SELECT por columna; vista diagnostics.delivery_tracking_incident |
| `delivery_work_session` | Sí | SELECT por columna; vista diagnostics.delivery_work_session |
| `deliveryman_advance` | Sí | SELECT por columna; vista diagnostics.deliveryman_advance |
| `deliveryman_day_state` | Sí | SELECT por columna; vista diagnostics.deliveryman_day_state |
| `deliveryman_location` | Sí | SELECT por columna; vista diagnostics.deliveryman_location |
| `discount_code` | Sí | SELECT por columna; vista diagnostics.discount_code |
| `duplicate_app_bank_payment_delete_backup` | No | Excluida: sin aislamiento tenant forzado |
| `electronic_invoice_branding` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `electronic_invoice_document` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `electronic_invoice_integration` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `email_outbox_message` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `employee_leave_attachment` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `employee_leave_request` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `employee_lunch` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `entity_audit_log` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `expense` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `expense_bank_payment` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `expense_category` | Sí | SELECT por columna; vista diagnostics.expense_category |
| `expense_detail` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `expense_header` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `expense_menu_target` | Sí | SELECT por columna; vista diagnostics.expense_menu_target |
| `expense_unit_conversion` | Sí | SELECT por columna; vista diagnostics.expense_unit_conversion |
| `external_delivery_order` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `initial_setup_backup_20260808_app_payments` | No | Excluida: sin aislamiento tenant forzado |
| `initial_setup_backup_20260808_bank_payments` | No | Excluida: sin aislamiento tenant forzado |
| `initial_setup_backup_20260808_expense_bank_payments` | No | Excluida: sin aislamiento tenant forzado |
| `initial_setup_backup_20260808_expense_details` | No | Excluida: sin aislamiento tenant forzado |
| `initial_setup_backup_20260808_expense_headers` | No | Excluida: sin aislamiento tenant forzado |
| `initial_setup_backup_20260808_order_details` | No | Excluida: sin aislamiento tenant forzado |
| `initial_setup_backup_20260808_orders` | No | Excluida: sin aislamiento tenant forzado |
| `initial_setup_backup_20260808_reservation_deposits` | No | Excluida: sin aislamiento tenant forzado |
| `integration_webhook_event` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `inventory_balance` | Sí | SELECT por columna; vista diagnostics.inventory_balance |
| `inventory_count` | Sí | SELECT por columna; vista diagnostics.inventory_count |
| `inventory_count_line` | Sí | SELECT por columna; vista diagnostics.inventory_count_line |
| `inventory_movement` | Sí | SELECT por columna; vista diagnostics.inventory_movement |
| `inventory_transfer` | Sí | SELECT por columna; vista diagnostics.inventory_transfer |
| `inventory_transfer_line` | Sí | SELECT por columna; vista diagnostics.inventory_transfer_line |
| `kitchen_voice_event` | Sí | SELECT por columna; vista diagnostics.kitchen_voice_event |
| `loyalty_cycle_step` | Sí | SELECT por columna; vista diagnostics.loyalty_cycle_step |
| `neighborhood` | Sí | SELECT por columna; vista diagnostics.neighborhood |
| `order` | Sí | SELECT por columna; vista diagnostics.order |
| `order_detail` | Sí | SELECT por columna; vista diagnostics.order_detail |
| `order_inventory_allocation` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `password_reset_token` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `payment_notification_outbox` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `print_job` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `product` | Sí | SELECT por columna; vista diagnostics.product |
| `product_category` | Sí | SELECT por columna; vista diagnostics.product_category |
| `product_expense_requirement` | Sí | SELECT por columna; vista diagnostics.product_expense_requirement |
| `rappi_availability_state` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `rappi_menu_publication` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `refresh_token` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `reservation_deposit` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `storefront_analytics_daily` | Sí | SELECT por columna; vista diagnostics.storefront_analytics_daily |
| `storefront_checkout` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `storefront_customer_auth_challenge` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `supplier` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `supplier_expense` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `tenant` | No | Excluida: sin aislamiento tenant forzado |
| `tenant_ai_setting` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `user` | Sí | SELECT(id,name,tenant_id); diagnostics.users expone únicamente id y name |
| `user_device_token` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `whatsapp_ai_invocation` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `whatsapp_branch_setting` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `whatsapp_channel_setting` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `whatsapp_commerce_event` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `whatsapp_commerce_outbox` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `whatsapp_commerce_session` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `whatsapp_commerce_session_token` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `whatsapp_conversation` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `whatsapp_flow_exchange` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `whatsapp_message` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `whatsapp_quick_reply` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `whatsapp_template` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `whatsapp_webhook_event` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `wompi_integration_audit` | No | Excluida: sin aislamiento tenant forzado |
| `wompi_payment_attempt` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `wompi_payment_integration` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `wompi_provider_transaction` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |
| `wompi_webhook_event` | Sí | Excluida: identidad, PII, finanzas, mensajes o payload operativo no aprobado |

## Columnas aprobadas

- `branch`: `id`, `tenant_id`, `name`, `is_active`, `created_at`, `updated_at`
- `branch_business_hour`: `id`, `tenant_id`, `branch_id`, `day_of_week`, `open_time`, `close_time`, `is_closed`, `display_order`, `created_at`, `updated_at`
- `commercial_profile`: `id`, `tenant_id`, `branch_id`, `name`, `description`, `ingredients`, `created_at`, `updated_at`
- `daily_promotion`: `id`, `tenant_id`, `branch_id`, `type`, `gift_product_id`, `discount_percentage`, `discount_scope`, `minimum_order_value`, `is_active`, `starts_at`, `ends_at`, `created_at`, `updated_at`
- `daily_promotion_product`: `id`, `tenant_id`, `daily_promotion_id`, `product_id`, `created_at`, `updated_at`
- `delivery_authorized_place`: `id`, `tenant_id`, `branch_id`, `active`, `radius_meters`, `created_at`, `updated_at`
- `delivery_device_event`: `id`, `tenant_id`, `event_type`, `battery_level_percent`, `internet_available`, `gps_enabled`, `recorded_at`, `synced_at`, `created_at`, `offline_location_count`
- `delivery_route`: `id`, `tenant_id`, `branch_id`, `status`, `last_assignment_at_utc`, `route_started_at_utc`, `planned_distance_meters`, `planned_driving_duration_seconds`, `stop_count`, `completed_at_utc`, `actual_duration_seconds`, `met_sla`, `created_at`, `updated_at`
- `delivery_route_proposal`: `id`, `tenant_id`, `delivery_routing_plan_id`, `sequence`, `status`, `recommendation`, `expected_departure_at_utc`, `wait_seconds`, `approximate_driving_duration_seconds`, `approximate_distance_meters`, `validated_driving_duration_seconds`, `validated_distance_meters`, `google_validation_status`, `score`, `created_at`, `updated_at`
- `delivery_route_proposal_stop`: `id`, `tenant_id`, `delivery_routing_plan_id`, `delivery_route_proposal_id`, `order_id`, `stop_sequence`, `estimated_ready_at_utc`, `estimated_arrival_at_utc`, `travel_from_previous_seconds`, `service_seconds`, `was_ready_at_generation`, `is_suggested_wait`, `created_at`, `updated_at`
- `delivery_route_stop`: `id`, `tenant_id`, `delivery_route_id`, `order_id`, `stop_sequence`, `arrival_confirmed_at_utc`, `auto_delivered_at_utc`, `created_at`, `updated_at`
- `delivery_routing_plan`: `id`, `tenant_id`, `branch_id`, `generation_number`, `status`, `generated_at_utc`, `available_slot_count`, `soon_slot_count`, `solver_duration_ms`, `matrix_source`, `created_at`, `updated_at`
- `delivery_tracking_alert`: `id`, `tenant_id`, `branch_id`, `alert_type`, `severity`, `status`, `occurred_at`, `occurrence_count`, `resolved_at`, `duration_seconds`, `created_at`, `updated_at`
- `delivery_tracking_incident`: `id`, `tenant_id`, `branch_id`, `incident_type`, `started_at`, `ended_at`, `duration_seconds`, `review_status`, `final_classification`, `created_at`, `updated_at`
- `delivery_work_session`: `id`, `tenant_id`, `branch_id`, `app_version`, `started_at`, `ended_at`, `status`, `created_at`, `updated_at`
- `deliveryman_advance`: `id`, `tenant_id`, `branch_id`, `amount`, `payment_method`, `created_at`, `updated_at`
- `deliveryman_day_state`: `id`, `tenant_id`, `branch_id`, `date`, `liquidation_mode`, `blocked`, `created_at`, `updated_at`
- `deliveryman_location`: `id`, `tenant_id`, `recorded_at`, `accuracy_meters`, `internet_available`, `gps_enabled`, `tracking_mode`, `created_at`, `synced_at`
- `discount_code`: `id`, `tenant_id`, `branch_id`, `type`, `gift_product_id`, `discount_percentage`, `starts_at`, `ends_at`, `minimum_order_value`, `is_active`, `created_at`, `updated_at`
- `expense_category`: `id`, `tenant_id`, `name`, `created_at`, `updated_at`
- `expense_menu_target`: `id`, `tenant_id`, `expense_id`, `target_type`, `target_id`, `created_at`, `updated_at`
- `expense_unit_conversion`: `id`, `tenant_id`, `expense_id`, `name`, `base_quantity`, `active`, `created_at`, `updated_at`
- `inventory_balance`: `id`, `tenant_id`, `branch_id`, `expense_id`, `quantity_on_hand`, `quantity_reserved`, `created_at`, `updated_at`
- `inventory_count`: `id`, `tenant_id`, `branch_id`, `status`, `created_at`, `updated_at`
- `inventory_count_line`: `id`, `tenant_id`, `inventory_count_id`, `expense_id`, `expected_quantity`, `counted_quantity`, `difference`, `created_at`, `updated_at`
- `inventory_movement`: `id`, `tenant_id`, `branch_id`, `expense_id`, `type`, `on_hand_delta`, `reserved_delta`, `order_id`, `transfer_id`, `inventory_count_id`, `created_at`, `updated_at`
- `inventory_transfer`: `id`, `tenant_id`, `source_branch_id`, `destination_branch_id`, `status`, `dispatched_at`, `received_at`, `created_at`, `updated_at`
- `inventory_transfer_line`: `id`, `tenant_id`, `transfer_id`, `expense_id`, `quantity`, `received_quantity`, `created_at`, `updated_at`
- `kitchen_voice_event`: `id`, `tenant_id`, `branch_id`, `order_id`, `kind`, `created_at`, `updated_at`
- `loyalty_cycle_step`: `id`, `tenant_id`, `branch_id`, `step_index`, `reward_label`, `step_name`, `reward_type`, `gift_product_id`, `discount_percentage`, `is_active`, `created_at`, `updated_at`
- `neighborhood`: `id`, `tenant_id`, `branch_id`, `name`, `delivery_fee`, `active`, `created_at`, `updated_at`
- `order`: `id`, `tenant_id`, `branch_id`, `origin_branch_id`, `type`, `status`, `subtotal`, `total`, `discount_total`, `delivery_fee`, `created_at`, `updated_at`, `reserved_for`, `prepare_at`, `order_source`, `inventory_issue`
- `order_detail`: `id`, `tenant_id`, `order_id`, `product_id`, `quantity`, `unit_price`, `discount`, `subtotal`, `created_at`, `updated_at`
- `product`: `id`, `tenant_id`, `category_id`, `name`, `price`, `stock`, `active`, `weight_grams`, `commercial_profile_id`, `serves_people_min`, `serves_people_max`, `storefront_variant_label`, `storefront_sort_order`, `inventory_enabled`, `inventory_control_mode`, `created_at`, `updated_at`
- `product_category`: `id`, `tenant_id`, `branch_id`, `name`, `storefront_role`, `created_at`, `updated_at`
- `product_expense_requirement`: `id`, `tenant_id`, `product_id`, `expense_id`, `base_quantity`, `created_at`, `updated_at`
- `storefront_analytics_daily`: `id`, `tenant_id`, `event_date`, `event_name`, `branch_id`, `fulfillment_type`, `payment_type`, `status_code`, `coverage_state`, `event_count`, `value_sum`, `created_at`, `updated_at`

## Aplicación a producción

1. Importar el SQL desde el SHA revisado con el workflow existente. #84 falló por `DO $`; no aplicó cambios.
2. Fusionar el PR importado y fijar SHA-256 normalizado.
3. Restaurar un respaldo reciente en una base aislada y registrar objeto, fecha, huella y verificaciones. La existencia de un backup o un deploy SUCCESS del cron no acredita restaurabilidad.
4. Obtener autorización expresa para ese archivo/SHA antes del comentario `APLICAR MIGRACION <sha256>`; no declarar `respaldo_verificado: SI` sin evidencia.
5. Verificar aislamiento y permisos con el lector real antes de activar. Mantener Actions como respaldo.

El cambio reduce columnas disponibles en consultas históricas de Actions (teléfonos, direcciones, GPS, notas, tokens, identidad y costos). Las consultas que dependan de ellas deberán pasar por autorización separada; el restaurante usa otro rol y sus permisos/políticas no se alteran.

Ampliación autorizada de nombres: `add_gateway_user_names_20261110.sql` concede solo `id`, `name` y `tenant_id` técnico en `public."user"`. La vista `diagnostics.users` devuelve exclusivamente `id` y `name` para resolver identidades de domiciliarios por ID; incluye los usuarios del tenant, sin filtrar ni publicar roles. Correo, teléfono, hashes, sesiones, dispositivos, nómina y demás campos siguen excluidos. FORCE RLS, guard restrictivo tenant=1, invoker/barrier y comprobaciones de privilegios son obligatorios. Aplicar después de la migración base mediante el mismo workflow con SHA y respaldo verificado.
