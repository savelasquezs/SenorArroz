BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '30s';

SELECT set_config('app.current_tenant_id', current_setting('app.rappi_test_tenant_id'), true);

CREATE TEMP TABLE rappi_test_manifest ON COMMIT DROP AS
SELECT current_setting('app.rappi_test_connection_id')::integer AS connection_id,
       current_setting('app.rappi_test_tenant_id')::integer AS tenant_id,
       trim(value) AS external_order_id,
       current_setting('app.rappi_test_started_at')::timestamptz AS started_at,
       current_setting('app.rappi_test_finished_at')::timestamptz AS finished_at
FROM unnest(string_to_array(current_setting('app.rappi_test_external_ids'), ',')) AS value;

CREATE TEMP TABLE rappi_test_targets ON COMMIT DROP AS
SELECT e.id AS external_id, e.internal_order_id AS order_id, e.external_order_id,
       e.connection_id, e.tenant_id, e.branch_id
FROM external_delivery_order e
JOIN rappi_test_manifest m ON m.connection_id = e.connection_id AND m.tenant_id = e.tenant_id
    AND m.external_order_id = e.external_order_id
JOIN delivery_app_connection c ON c.id = e.connection_id AND c.tenant_id = e.tenant_id
WHERE c.provider = 'rappi' AND c.environment = 'sandbox'
    AND e.created_at BETWEEN m.started_at AND m.finished_at;

DO $$
BEGIN
    IF current_setting('app.rappi_test_cleanup_approved', true) IS DISTINCT FROM 'RAPPI_TEST_CLEANUP' THEN
        RAISE EXCEPTION 'Falta autorización explícita de limpieza de los IDs de prueba';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM rappi_test_manifest WHERE external_order_id <> '')
       OR (SELECT count(*) FROM rappi_test_manifest) <> (SELECT count(*) FROM rappi_test_targets) THEN
        RAISE EXCEPTION 'Los IDs, tenant, conexión sandbox o ventana temporal no coinciden completamente';
    END IF;
END $$;

SELECT pg_advisory_xact_lock(1380012112, hashtext(tenant_id || ':' || connection_id || ':' || external_order_id))
FROM rappi_test_targets ORDER BY external_order_id;

UPDATE rappi_test_targets t SET order_id = e.internal_order_id
FROM external_delivery_order e WHERE e.id = t.external_id AND e.tenant_id = t.tenant_id;

SELECT o.id FROM "order" o JOIN rappi_test_targets t ON t.order_id = o.id
WHERE o.tenant_id = t.tenant_id ORDER BY o.id FOR UPDATE OF o;

CREATE TEMP TABLE rappi_test_print_jobs ON COMMIT DROP AS
SELECT p.id FROM print_job p WHERE EXISTS (
    SELECT 1 FROM jsonb_array_elements_text(p.order_ids_json) AS v
    JOIN rappi_test_targets t ON t.order_id::text = v.value AND t.tenant_id = p.tenant_id);
SELECT p.id FROM print_job p JOIN rappi_test_print_jobs t ON t.id = p.id ORDER BY p.id FOR UPDATE OF p;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM external_delivery_order e JOIN rappi_test_targets t ON t.external_id = e.id
               WHERE e.status IN ('Processing','PendingAcceptance','SyncError'))
       OR EXISTS (SELECT 1 FROM integration_webhook_event e JOIN rappi_test_targets t
                  ON e.connection_id = t.connection_id AND e.tenant_id = t.tenant_id
                  WHERE e.event_key = 'LOCAL_RAPPI_ACCEPT_EFFECTS:' || t.connection_id || ':' || t.external_order_id
                    AND e.status NOT IN ('processed','failed'))
       OR EXISTS (SELECT 1 FROM app_payment p JOIN rappi_test_targets t ON t.order_id = p.order_id
               WHERE p.is_setted OR p.actual_settled_amount IS NOT NULL)
       OR EXISTS (SELECT 1 FROM bank_payment p JOIN rappi_test_targets t ON t.order_id = p.order_id)
       OR EXISTS (SELECT 1 FROM print_job p JOIN rappi_test_print_jobs t ON t.id = p.id
                  WHERE p.status::text IN ('claimed','printing','processing','Claimed','Printing','Processing')
                     OR EXISTS (SELECT 1 FROM jsonb_array_elements_text(p.order_ids_json) AS v
                                WHERE NOT EXISTS (SELECT 1 FROM rappi_test_targets m WHERE m.order_id::text = v.value
                                                  AND m.tenant_id = p.tenant_id)))
       OR EXISTS (SELECT 1 FROM "order" o JOIN rappi_test_targets t ON t.order_id = o.id
                  WHERE o.order_source IS DISTINCT FROM 'rappi' OR o.delivery_route_id IS NOT NULL
                     OR o.delivery_man_id IS NOT NULL) THEN
        RAISE EXCEPTION 'El pedido tiene liquidaciones, movimientos bancarios o ruta interna; requiere revisión específica';
    END IF;
END $$;

CREATE TEMP TABLE rappi_test_inventory_delta ON COMMIT DROP AS
SELECT m.tenant_id, m.branch_id, m.expense_id, sum(m.on_hand_delta) AS on_hand_delta,
       sum(m.reserved_delta) AS reserved_delta, sum(m.on_hand_delta * m.unit_cost) AS cost_delta
FROM inventory_movement m JOIN rappi_test_targets t ON t.order_id = m.order_id AND t.tenant_id = m.tenant_id
GROUP BY m.tenant_id, m.branch_id, m.expense_id;

SELECT b.id FROM inventory_balance b JOIN rappi_test_inventory_delta d
ON d.tenant_id = b.tenant_id AND d.branch_id = b.branch_id AND d.expense_id = b.expense_id
ORDER BY b.branch_id, b.expense_id FOR UPDATE OF b;

UPDATE inventory_balance b
SET quantity_on_hand = b.quantity_on_hand - d.on_hand_delta,
    quantity_reserved = b.quantity_reserved - d.reserved_delta,
    average_unit_cost = CASE WHEN b.quantity_on_hand - d.on_hand_delta > 0
        THEN GREATEST(0, (b.quantity_on_hand * b.average_unit_cost - d.cost_delta)
            / (b.quantity_on_hand - d.on_hand_delta)) ELSE b.average_unit_cost END,
    updated_at = now()
FROM rappi_test_inventory_delta d
WHERE d.tenant_id = b.tenant_id AND d.branch_id = b.branch_id AND d.expense_id = b.expense_id;

UPDATE integration_webhook_event e SET status = 'processed', processed_at = now(), updated_at = now(),
    last_error = NULL, payload_json = '{"test_cleanup":true}'
WHERE EXISTS (SELECT 1 FROM rappi_test_targets t WHERE e.connection_id = t.connection_id AND e.tenant_id = t.tenant_id
    AND (e.event_key IN ('READY_FOR_PICKUP:' || t.connection_id || ':' || t.external_order_id,
                        'LOCAL_RAPPI_ACCEPT_EFFECTS:' || t.connection_id || ':' || t.external_order_id)
        OR e.event_type = 'LOCAL_RAPPI_CANCEL_EFFECTS' AND e.payload_json::jsonb #>> '{snapshot,orderId}' = t.order_id::text));

UPDATE external_delivery_order e SET internal_order_id = NULL, status = 'Cancelled',
    customer_name = 'Prueba eliminada', customer_phone = NULL, delivery_address = NULL,
    raw_payload_json = '{}', lines_json = '[]', discounts_json = '[]', validation_errors_json = NULL,
    total = 0, total_products = 0, total_discounts = 0, total_discount_by_partner = 0,
    total_discount_by_rappi = 0, total_charges = 0, last_error = 'Prueba eliminada tras homologación',
    pii_purged_at = now(), updated_at = now()
FROM rappi_test_targets t WHERE e.id = t.external_id AND e.tenant_id = t.tenant_id;

DELETE FROM print_job p USING rappi_test_print_jobs t WHERE p.id = t.id;
DELETE FROM kitchen_voice_event p USING rappi_test_targets t WHERE p.order_id = t.order_id AND p.tenant_id = t.tenant_id;
DELETE FROM order_inventory_allocation p USING rappi_test_targets t WHERE p.order_id = t.order_id AND p.tenant_id = t.tenant_id;
DELETE FROM inventory_movement p USING rappi_test_targets t WHERE p.order_id = t.order_id AND p.tenant_id = t.tenant_id;
DELETE FROM app_payment p USING rappi_test_targets t WHERE p.order_id = t.order_id AND p.tenant_id = t.tenant_id;
DELETE FROM order_detail p USING rappi_test_targets t WHERE p.order_id = t.order_id AND p.tenant_id = t.tenant_id;
DELETE FROM "order" p USING rappi_test_targets t WHERE p.id = t.order_id AND p.tenant_id = t.tenant_id;

COMMIT;
