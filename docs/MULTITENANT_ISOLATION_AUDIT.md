# Auditoría de aislamiento multitenant v2

## Resultado

El modelo EF Core contiene 94 entidades: `Tenant` es la única entidad global y las otras 93 implementan `ITenantOwned`. Todas las entidades tenant-owned tienen `tenant_id` obligatorio, índice simple, filtro global y guard de escritura centralizado. No se implementó RLS en este bloque.

El tenant se obtiene del JWT o de un scope interno explícito. Un request sin tenant ve cero filas tenant-owned y no puede escribir. `BeginSystemScope()` se reserva para resolución controlada de tenant, limpieza global y selección de trabajo; `BeginTenantScope(id)` delimita el procesamiento real.

## Matriz completa

En la columna EF, `F+G` significa filtro global y guard de escritura. `Relación` significa backfill desde una entidad propietaria y precheck de consistencia. `Raíz` significa que las filas históricas sin relación sólo se asignan automáticamente cuando existe exactamente un tenant; de lo contrario el script aborta.

| Dominio | Entidades y tablas | Ownership | EF | Backfill y FK | Workers / excepción / pendiente |
|---|---|---|---|---|---|
| Control plane | `Tenant` (`tenant`) | Global | Sin filtro tenant | Raíz de todas las FK | Única excepción global; RLS queda para Bloque 3 |
| Organización e identidad | `Branch` (`branch`), `BranchBusinessHour` (`branch_business_hour`), `BranchAiSetting` (`branch_ai_setting`), `BranchPrintSettings` (`branch_print_settings`), `BusinessDocument` (`business_document`), `TenantAiSetting` (`tenant_ai_setting`), `User` (`user`), `RefreshToken` (`refresh_token`), `PasswordResetToken` (`password_reset_token`), `UserDeviceToken` (`user_device_token`) | Tenant | F+G | Branch/document/settings: raíz o branch; credenciales: user; `NOT NULL`, índice y FK a tenant | Auth hace lookup inicial en system scope y vuelve a tenant scope; limpiezas de tokens usan system scope explícito |
| Clientes y direcciones | `Customer` (`customer`), `CustomerPhone` (`customer_phone`), `CustomerMergeHistory` (`customer_merge_history`), `Address` (`address`), `AddressBranch` (`address_branch`), `Neighborhood` (`neighborhood`), `StorefrontCustomerAuthChallenge` (`storefront_customer_auth_challenge`) | Tenant | F+G | branch → customer/neighborhood; customer → phone/address/history; address → address_branch; challenge: raíz segura | Storefront usa tenant configurado por servidor; no acepta tenant del cliente |
| Catálogo y beneficios | `ProductCategory` (`product_category`), `Product` (`product`), `CommercialProfile` (`commercial_profile`), `DailyPromotion` (`daily_promotion`), `DailyPromotionProduct` (`daily_promotion_product`), `DiscountCode` (`discount_code`), `LoyaltyCycleStep` (`loyalty_cycle_step`) | Tenant | F+G | branch → categorías/perfiles/promociones/beneficios; categoría → producto; promoción → detalle | Sin worker global; todas las consultas quedan filtradas |
| Pedidos y pagos operativos | `Order` (`order`), `OrderDetail` (`order_detail`), `ReservationDeposit` (`reservation_deposit`), `Bank` (`bank`), `App` (`app`), `BankPayment` (`bank_payment`), `AppPayment` (`app_payment`), `BankTransfer` (`bank_transfer`) | Tenant | F+G | branch → order/bank; order → detalles/pagos; bank → app; user → transfer; FK compuesta crítica order/detail | SQL de pedidos por fecha incluye tenant explícito |
| Caja y préstamos | `CashRegisterClosure` (`cash_register_closure`), `CashClosureBankReconciliation` (`cash_closure_bank_reconciliation`), `CashClosureInformalLoan` (`cash_closure_informal_loan`), `CashVaultMovement` (`cash_vault_movement`), `BranchInformalLoan` (`branch_informal_loan`), `BranchInformalLoanExemptOrder` (`branch_informal_loan_exempt_order`) | Tenant | F+G | branch → cierres/movimientos/préstamos; cierre/préstamo → dependientes | Sin excepción global |
| Gastos y proveedores | `ExpenseCategory` (`expense_category`), `Expense` (`expense`), `ExpenseHeader` (`expense_header`), `ExpenseDetail` (`expense_detail`), `ExpenseBankPayment` (`expense_bank_payment`), `ExpenseMenuTarget` (`expense_menu_target`), `Supplier` (`supplier`), `SupplierExpense` (`supplier_expense`) | Tenant | F+G | categorías/proveedor: raíz segura; categoría → expense; header/expense → dependientes | Sin excepción global |
| Domicilios y tracking | `DeliverymanAdvance` (`deliveryman_advance`), `DeliverymanDayState` (`deliveryman_day_state`), `DeliverymanLocation` (`deliveryman_location`), `DeliveryDeviceEvent` (`delivery_device_event`), `DeliveryAuthorizedPlace` (`delivery_authorized_place`), `DeliveryStay` (`delivery_stay`), `DeliveryTrackingIncident` (`delivery_tracking_incident`), `DeliveryTrackingAlert` (`delivery_tracking_alert`), `DeliveryIncidentLocationEvidence` (`delivery_incident_location_evidence`), `DeliveryIncidentDeviceEventEvidence` (`delivery_incident_device_event_evidence`), `DeliveryWorkSession` (`delivery_work_session`), `DeliveryRoute` (`delivery_route`), `DeliveryRouteStop` (`delivery_route_stop`), `DeliveryRoutingPlan` (`delivery_routing_plan`), `DeliveryRouteProposal` (`delivery_route_proposal`), `DeliveryRouteProposalStop` (`delivery_route_proposal_stop`) | Tenant | F+G | branch/user/work session/incident/route/plan según jerarquía; prechecks de hijos críticos | Alertas, permanencias, consolidación de rutas y autocierre iteran tenants activos en scopes nuevos |
| WhatsApp | `WhatsAppBranchSetting` (`whatsapp_branch_setting`), `WhatsAppChannelSetting` (`whatsapp_channel_setting`), `WhatsAppConversation` (`whatsapp_conversation`), `WhatsAppMessage` (`whatsapp_message`), `WhatsAppQuickReply` (`whatsapp_quick_reply`), `WhatsAppTemplate` (`whatsapp_template`), `WhatsAppWebhookEvent` (`whatsapp_webhook_event`), `WhatsAppAiInvocation` (`whatsapp_ai_invocation`), `WhatsAppCommerceSession` (`whatsapp_commerce_session`), `WhatsAppCommerceSessionToken` (`whatsapp_commerce_session_token`), `WhatsAppFlowExchange` (`whatsapp_flow_exchange`), `WhatsAppCommerceOutboxMessage` (`whatsapp_commerce_outbox`), `WhatsAppCommerceEvent` (`whatsapp_commerce_event`) | Tenant | F+G | branch/conversation/channel/session según relación; raíces sólo con tenant único | Webhooks resuelven tenant por configuración persistida en system scope; IA, recovery, telemetría y outbox procesan dentro de tenant scope |
| Rappi | `DeliveryAppConnection` (`delivery_app_connection`), `DeliveryAppStore` (`delivery_app_store`), `DeliveryAppWebhookSubscription` (`delivery_app_webhook_subscription`), `DeliveryAppProductMapping` (`delivery_app_product_mapping`), `ExternalDeliveryOrder` (`external_delivery_order`), `IntegrationWebhookEvent` (`integration_webhook_event`), `RappiMenuPublication` (`rappi_menu_publication`), `RappiAvailabilityState` (`rappi_availability_state`) | Tenant | F+G | branch → conexión; conexión → todos los dependientes; FK compuesta crítica | Webhook resuelve `PublicId` en system scope y procesa en el tenant persistido; worker itera tenants activos |
| Wompi y checkout | `WompiPaymentIntegration` (`wompi_payment_integration`), `WompiPaymentAttempt` (`wompi_payment_attempt`), `StorefrontCheckout` (`storefront_checkout`), `WompiProviderTransaction` (`wompi_provider_transaction`), `WompiWebhookEvent` (`wompi_webhook_event`), `PaymentNotificationOutboxMessage` (`payment_notification_outbox`) | Tenant | F+G | branch → integración/checkout/outbox; integración → intento/webhook; intento → transacción; FK compuestas críticas | Webhook deriva tenant de la referencia persistida; outbox selecciona globalmente y procesa por tenant |
| Operación técnica | `PrintJob` (`print_job`), `EmailOutboxMessage` (`email_outbox_message`), `DailyAuditDispatch` (`daily_audit_dispatch`), `EntityAuditLog` (`entity_audit_log`) | Tenant | F+G | branch o raíz segura; trigger de auditoría deriva tenant desde branch | Email outbox itera tenants; dispatch/auditoría quedan filtrados; impresión conserva branch dentro del tenant |

## Escrituras

- Alta con `TenantId = 0`: se asigna el tenant actual.
- Alta con tenant diferente: se rechaza.
- Cambio de `TenantId`: se rechaza.
- Borrado de entidad de otro tenant: se rechaza.
- Sin tenant: se rechaza toda escritura tenant-owned.
- System scope: puede mantener varios tenants, pero un alta debe traer `TenantId > 0` explícito.
- `tenant_id` es concurrency token para incluirlo en actualizaciones y borrados desconectados.

## SQL y despliegue

`SenorArroz.Infrastructure/Scripts/multitenant_isolation_v2.sql` es idempotente y transaccional. Agrega columnas nullable, elimina constraints heredados `tenant_id = 1`, hace backfill por relaciones confiables, aborta ante ambigüedad o relaciones cross-tenant, elimina defaults, aplica `NOT NULL`, índices y FK a `tenant`, y crea FK compuestas para relaciones críticas.

Orden de despliegue recomendado:

1. Respaldo y ejecución del script en una copia reciente de producción.
2. Revisar que los prechecks terminen sin excepción y que no queden nulos.
3. Desplegar backend con filtros/guards y configuración tenant válida.
4. Validar login, storefront, WhatsApp, Wompi, Rappi, impresión y workers con dos tenants.
5. Ejecutar el script en producción dentro de la ventana acordada.

## Pendiente para Bloque 3

- PostgreSQL RLS y variables de sesión por tenant.
- Unicidades funcionales compuestas adicionales tras auditar duplicados reales.
- Aislamiento de SignalR, archivos y credenciales del agente de impresión.
- Pruebas PostgreSQL de concurrencia en CI con Docker obligatorio.
- Control plane SaaS: altas, invitaciones, planes, suscripciones, metering y portal `/platform`.
