\set ON_ERROR_STOP on
BEGIN;

SELECT pg_advisory_xact_lock(1380012113, 1);

DO $$
DECLARE
    source delivery_app_connection%ROWTYPE;
    target delivery_app_connection%ROWTYPE;
BEGIN
    SELECT c.* INTO STRICT source
    FROM delivery_app_connection c
    JOIN branch b ON b.id = c.branch_id AND b.tenant_id = c.tenant_id
    JOIN tenant t ON t.id = c.tenant_id
    JOIN app a ON a.id = c.financial_app_id AND a.tenant_id = c.tenant_id AND a.active
    JOIN bank k ON k.id = a.bank_id AND k.branch_id = c.branch_id AND k.tenant_id = c.tenant_id
    JOIN customer u ON u.id = c.customer_id AND u.tenant_id = c.tenant_id AND u.active
    JOIN "user" v ON v.id = c.technical_user_id AND v.branch_id = c.branch_id AND v.tenant_id = c.tenant_id
    WHERE c.tenant_id = 1 AND c.branch_id = 1 AND c.provider = 'rappi'
      AND c.environment = 'sandbox' AND b.name = 'Santander' AND b.is_active AND t.is_active;

    INSERT INTO delivery_app_connection (
        tenant_id, branch_id, provider, environment, display_name, financial_app_id,
        customer_id, technical_user_id, default_cooking_time_minutes,
        estimated_commission_rate, pii_retention_days, is_active, is_verified
    ) VALUES (
        source.tenant_id, source.branch_id, 'rappi', 'production', 'Rappi Santander', source.financial_app_id,
        source.customer_id, source.technical_user_id, source.default_cooking_time_minutes,
        source.estimated_commission_rate, source.pii_retention_days, false, false
    ) ON CONFLICT (branch_id, provider, environment) DO NOTHING;

    SELECT * INTO STRICT target FROM delivery_app_connection
    WHERE tenant_id = 1 AND branch_id = 1 AND provider = 'rappi' AND environment = 'production';

    IF target.financial_app_id IS DISTINCT FROM source.financial_app_id
        OR target.customer_id IS DISTINCT FROM source.customer_id
        OR target.technical_user_id IS DISTINCT FROM source.technical_user_id THEN
        RAISE EXCEPTION 'La conexión productiva existente requiere revisión de sus referencias internas.';
    END IF;

    IF EXISTS (
        SELECT 1 FROM delivery_app_store s JOIN delivery_app_connection c ON c.id = s.connection_id
        WHERE c.provider = 'rappi' AND c.environment = 'production' AND c.id <> target.id
          AND (s.rappi_store_id = '900403973' OR s.store_integration_id = '900403973')
    ) OR EXISTS (
        SELECT 1 FROM delivery_app_store WHERE connection_id = target.id
          AND (tenant_id <> 1 OR rappi_store_id <> '900403973'
               OR store_integration_id IS DISTINCT FROM '900403973' OR NOT is_parent)
    ) THEN
        RAISE EXCEPTION 'La tienda productiva ya está asignada o la conexión contiene otras tiendas.';
    END IF;

    INSERT INTO delivery_app_store (
        tenant_id, connection_id, rappi_store_id, store_integration_id, name,
        is_parent, manual_ready_for_pickup_enabled
    ) VALUES (1, target.id, '900403973', '900403973', 'Señor Arroz Santander', true, false)
    ON CONFLICT (connection_id, rappi_store_id) DO NOTHING;
END $$;

COMMIT;

SELECT c.id, c.tenant_id, c.branch_id, c.environment, c.is_active, c.is_verified,
       s.rappi_store_id, s.store_integration_id, s.manual_ready_for_pickup_enabled
FROM delivery_app_connection c JOIN delivery_app_store s ON s.connection_id = c.id
WHERE c.tenant_id = 1 AND c.branch_id = 1 AND c.provider = 'rappi' AND c.environment = 'production';
