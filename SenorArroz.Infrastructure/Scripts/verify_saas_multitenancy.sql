DO $$
DECLARE item record;
BEGIN
    IF (SELECT count(*) FROM tenant WHERE slug = 'senor-arroz') <> 1 THEN
        RAISE EXCEPTION 'Debe existir exactamente un tenant senor-arroz.';
    END IF;
    IF (SELECT count(*) FROM saas_plan WHERE code IN ('essential', 'professional', 'unlimited')) <> 3 THEN
        RAISE EXCEPTION 'El catÃ¡logo de planes SaaS estÃ¡ incompleto.';
    END IF;
    IF (SELECT count(*) FROM saas_module) <> 21 THEN
        RAISE EXCEPTION 'El catÃ¡logo de mÃ³dulos SaaS estÃ¡ incompleto.';
    END IF;
    IF NOT EXISTS (
        SELECT 1
        FROM platform_user platform
        JOIN "user" operational ON lower(operational.email) = lower(platform.email)
        WHERE lower(platform.email) = 'santyvano@outlook.com'
          AND platform.password_hash = operational.password_hash
    ) THEN
        RAISE EXCEPTION 'La identidad PlatformAdmin no conserva el hash del usuario operativo.';
    END IF;
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public' AND column_name = 'tenant_id' AND is_nullable = 'YES'
          AND table_name NOT IN ('tenant_subscription', 'tenant_addon', 'tenant_invitation', 'tenant_usage_monthly')
    ) THEN
        RAISE EXCEPTION 'Existen tablas operativas con tenant_id nullable.';
    END IF;
    IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname LIKE 'fk_mt_%' AND NOT convalidated) THEN
        RAISE EXCEPTION 'Existen claves forÃ¡neas multitenant sin validar.';
    END IF;
    FOR item IN
        SELECT table_schema, table_name
        FROM information_schema.columns
        WHERE table_schema = 'public' AND column_name = 'tenant_id'
          AND table_name NOT IN ('tenant_subscription', 'tenant_addon', 'tenant_invitation', 'tenant_usage_monthly')
    LOOP
        IF NOT EXISTS (
            SELECT 1
            FROM pg_class relation
            JOIN pg_namespace namespace ON namespace.oid = relation.relnamespace
            WHERE namespace.nspname = item.table_schema AND relation.relname = item.table_name
              AND relation.relrowsecurity AND relation.relforcerowsecurity
        ) THEN
            RAISE EXCEPTION 'RLS/FORCE RLS ausente en %.%', item.table_schema, item.table_name;
        END IF;
        IF NOT EXISTS (
            SELECT 1 FROM pg_policies
            WHERE schemaname = item.table_schema AND tablename = item.table_name AND policyname = 'tenant_isolation'
        ) THEN
            RAISE EXCEPTION 'PolÃ­tica tenant_isolation ausente en %.%', item.table_schema, item.table_name;
        END IF;
    END LOOP;
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_branch_tenant_quota' AND NOT tgisinternal)
       OR NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_user_tenant_quota' AND NOT tgisinternal) THEN
        RAISE EXCEPTION 'Los triggers transaccionales de cuotas no estÃ¡n activos.';
    END IF;
END $$;
