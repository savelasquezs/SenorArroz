\set ON_ERROR_STOP on
DO $$ BEGIN
    IF current_database() <> 'senor_arroz_delivery_dev' THEN
        RAISE EXCEPTION 'Este script solo admite la base de desarrollo de delivery_app';
    END IF;
END $$;
CREATE EXTENSION IF NOT EXISTS pgcrypto;
SELECT set_config('app.system_scope', 'on', false);
BEGIN;
INSERT INTO "user" (tenant_id, branch_id, role, name, email, phone, password_hash, active, web_access_enabled)
SELECT 1, b.id, u.role, u.name, u.email, u.phone, crypt(:'test_password', gen_salt('bf', 10)), true, u.role <> 'deliveryman'
FROM branch b CROSS JOIN (VALUES
    ('deliveryman', 'Domiciliario de pruebas', 'domiciliario@pruebas.local', '3000000001'),
    ('admin', 'Administrador de pruebas', 'admin@pruebas.local', '3000000002')
) u(role,name,email,phone)
WHERE b.tenant_id=1 AND b.name='Santander'
AND NOT EXISTS (SELECT 1 FROM "user" existing WHERE existing.tenant_id=1 AND existing.email=u.email);

UPDATE branch SET latitude=6.309, longitude=-75.581, phone1='0000000000'
WHERE tenant_id=1 AND name='Santander' AND latitude IS NULL;

DO $$
DECLARE b integer; actor integer; customer_row record; order_id integer; product_row record; n integer := 0; state text;
BEGIN
    SELECT id INTO b FROM branch WHERE tenant_id=1 AND name='Santander';
    SELECT id INTO actor FROM "user" WHERE tenant_id=1 AND email='admin@pruebas.local';
    SELECT id,price INTO product_row FROM product WHERE tenant_id=1 AND active ORDER BY id LIMIT 1;
    FOR customer_row IN SELECT a.id address_id,a.customer_id,a.delivery_fee
        FROM address a WHERE a.tenant_id=1 ORDER BY a.id LIMIT 8
    LOOP
        n := n+1;
        IF EXISTS (SELECT 1 FROM "order" WHERE tenant_id=1 AND notes='Prueba local de rutas #'||n) THEN CONTINUE; END IF;
        state := CASE WHEN n<=6 THEN 'ready' ELSE 'in_preparation' END;
        INSERT INTO "order" (tenant_id,branch_id,taken_by_id,customer_id,address_id,type,status,status_times,subtotal,total,delivery_fee,notes,created_at)
        VALUES (1,b,actor,customer_row.customer_id,customer_row.address_id,'delivery',state,
            jsonb_build_object('taken',now()-interval '20 minutes','inpreparation',now()-interval '15 minutes')
                || CASE WHEN n<=6 THEN jsonb_build_object('ready',now()-interval '1 minute') ELSE '{}'::jsonb END,
            product_row.price,product_row.price+customer_row.delivery_fee,customer_row.delivery_fee,'Prueba local de rutas #'||n,now()-interval '20 minutes')
        RETURNING id INTO order_id;
        INSERT INTO order_detail (tenant_id,order_id,product_id,quantity,unit_price)
        VALUES (1,order_id,product_row.id,1,product_row.price);
    END LOOP;
END $$;
COMMIT;
