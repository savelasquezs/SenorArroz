\set ON_ERROR_STOP on
DO $$ BEGIN
    IF current_database() <> 'senor_arroz_delivery_dev' THEN
        RAISE EXCEPTION 'Este script solo admite la base de desarrollo de delivery_app';
    END IF;
END $$;
SELECT set_config('app.system_scope', 'on', false);
BEGIN;
CREATE TEMP TABLE route_test_users (slot integer, user_id integer) ON COMMIT DROP;
INSERT INTO "user" (tenant_id, branch_id, role, name, email, phone, password_hash, active, web_access_enabled, delivery_app_enabled)
SELECT 1, 1, 'deliveryman', v.name, v.email, v.phone, crypt(:'test_password', gen_salt('bf', 10)), true, false, true
FROM (VALUES
    (1, 'Domiciliario real libre 1', 'domiciliario.real1@pruebas.local', '3000000011'),
    (2, 'Domiciliario con entrega previa', 'domiciliario.real2@pruebas.local', '3000000012'),
    (3, 'Domiciliario en ruta', 'domiciliario.real3@pruebas.local', '3000000013'),
    (4, 'Domiciliario real libre 2', 'domiciliario.real4@pruebas.local', '3000000014')
) v(slot,name,email,phone)
WHERE NOT EXISTS (SELECT 1 FROM "user" u WHERE u.email=v.email);
INSERT INTO route_test_users(slot,user_id)
SELECT v.slot, u.id FROM (VALUES
    (1, 'domiciliario.real1@pruebas.local'), (2, 'domiciliario.real2@pruebas.local'),
    (3, 'domiciliario.real3@pruebas.local'), (4, 'domiciliario.real4@pruebas.local')
) v(slot,email) JOIN "user" u ON u.email=v.email;

INSERT INTO delivery_work_session (deliveryman_id, branch_id, device_installation_id, device_platform, device_description, app_version, started_at, auto_close_at, status, last_communication_at, tenant_id)
SELECT r.user_id, 1, 'route-test-'||r.slot, 'android', 'Prueba rutas reales', 'local-test', now()-interval '10 minutes', now()+interval '10 hours', 'active', now(), 1
FROM route_test_users r
WHERE NOT EXISTS (SELECT 1 FROM delivery_work_session s WHERE s.deliveryman_id=r.user_id AND s.status='active');

INSERT INTO deliveryman_location (tenant_id, deliveryman_id, work_session_id, recorded_at, latitude, longitude, accuracy_meters, tracking_mode)
SELECT 1, r.user_id, s.id, now(), 6.308047 + r.slot*0.00001, -75.574383 + r.slot*0.00001, 8, 'light'
FROM route_test_users r JOIN delivery_work_session s ON s.deliveryman_id=r.user_id AND s.status='active'
WHERE NOT EXISTS (SELECT 1 FROM deliveryman_location l WHERE l.work_session_id=s.id AND l.recorded_at >= now()-interval '30 seconds');

INSERT INTO address (tenant_id,customer_id,address,delivery_fee,latitude,longitude,is_primary,original_address,normalized_address,validation_source,validated_at)
SELECT 1, 1, v.address, 0, v.latitude, v.longitude, false, v.address, lower(v.address), 'local_test', now()
FROM (VALUES
 ('METTAGRUP SAS',6.230381,-75.570792),('Kra 69a#93-110',6.288625,-75.573932),('Carrera 74a #94-53',6.291033,-75.578738),('Carrera 67#97-123',6.293091,-75.570820),('Cra 73a #93-76',6.289582,-75.577574),('Calle 78 #79a-3',6.280336,-75.585504),('Carrera 68 94-97',6.289412,-75.572831),('Cr 72 #95 107',6.291668,-75.575081),('hospital Pablo Tobón',6.276844,-75.579704),('Cra67#106b-4',6.303879,-75.566611),('Calle 95#79 a 32',6.294037,-75.583867),('calle 103 # 65 34',6.297360,-75.567386),('Cll 114a 63 D 38',6.309520,-75.559221),('Calle 94 # 74 a 27',6.290600,-75.579075),('Parque Girardota',6.377581,-75.446200),('Calle 115 #75a12',6.310454,-75.570210),('Calle 77 c 86a46',6.283697,-75.590655),('Calle 104 a #80-12',6.304766,-75.577928),('Calle 89 b # 71 b 28',6.284402,-75.578441),('Cra90d 76-20',6.284744,-75.595465)
) v(address,latitude,longitude)
WHERE NOT EXISTS (SELECT 1 FROM address a WHERE a.tenant_id=1 AND a.customer_id=1 AND a.address=v.address);

DO $$
DECLARE admin_id integer; product_id integer; product_price integer; route_id integer; order_id integer; address_id integer; customer_id integer; i integer;
DECLARE driver_prev integer; driver_route integer; session_prev integer; session_route integer;
BEGIN
    SELECT id INTO admin_id FROM "user" WHERE email='admin@pruebas.local';
    SELECT p.id, p.price INTO product_id, product_price FROM product p WHERE p.tenant_id=1 AND p.active ORDER BY p.id LIMIT 1;
    SELECT user_id INTO driver_prev FROM route_test_users WHERE slot=2;
    SELECT user_id INTO driver_route FROM route_test_users WHERE slot=3;
    SELECT id INTO session_prev FROM delivery_work_session WHERE deliveryman_id=driver_prev AND status='active';
    SELECT id INTO session_route FROM delivery_work_session WHERE deliveryman_id=driver_route AND status='active';

    SELECT a.id,a.customer_id INTO address_id,customer_id FROM address a WHERE a.tenant_id=1 AND a.address='METTAGRUP SAS' LIMIT 1;
    INSERT INTO "order" (tenant_id,branch_id,taken_by_id,customer_id,address_id,type,status,status_times,subtotal,total,delivery_fee,delivery_man_id,created_at,updated_at,notes)
    VALUES (1,1,admin_id,customer_id,address_id,'delivery','delivered',jsonb_build_object('taken',now()-interval '90 minutes','ready',now()-interval '80 minutes','delivered',now()-interval '55 minutes'),product_price,product_price,0,driver_prev,now()-interval '90 minutes',now()-interval '55 minutes','Prueba real entrega previa') RETURNING id INTO order_id;
    INSERT INTO delivery_route (deliveryman_id,branch_id,status,last_assignment_at_utc,route_started_at_utc,completed_at_utc,stop_count,tenant_id,work_session_id)
    VALUES (driver_prev,1,'completed',now()-interval '80 minutes',now()-interval '75 minutes',now()-interval '55 minutes',1,1,session_prev) RETURNING id INTO route_id;
    UPDATE "order" SET delivery_route_id=route_id WHERE id=order_id;
    INSERT INTO delivery_route_stop (delivery_route_id,order_id,stop_sequence,address_snapshot_text,tenant_id) VALUES (route_id,order_id,1,'METTAGRUP SAS, San diego',1);

    FOR i IN 1..2 LOOP
        SELECT a.id,a.customer_id INTO address_id,customer_id FROM address a WHERE a.tenant_id=1 AND a.address=(ARRAY['Kra 69a#93-110','Carrera 74a #94-53'])[i] LIMIT 1;
        INSERT INTO "order" (tenant_id,branch_id,taken_by_id,customer_id,address_id,type,status,status_times,subtotal,total,delivery_fee,delivery_man_id,created_at,updated_at,notes)
        VALUES (1,1,admin_id,customer_id,address_id,'delivery','ready',jsonb_build_object('taken',now()-interval '20 minutes','ready',now()-interval '10 minutes'),product_price,product_price,0,driver_route,now()-interval '20 minutes',now(),'Prueba real ruta activa #'||i) RETURNING id INTO order_id;
        IF i=1 THEN
            INSERT INTO delivery_route (deliveryman_id,branch_id,status,last_assignment_at_utc,stop_count,tenant_id,work_session_id)
            VALUES (driver_route,1,'open',now()-interval '5 minutes',2,1,session_route) RETURNING id INTO route_id;
        END IF;
        UPDATE "order" SET delivery_route_id=route_id WHERE id=order_id;
        INSERT INTO delivery_route_stop (delivery_route_id,order_id,stop_sequence,address_snapshot_text,tenant_id) VALUES (route_id,order_id,i,CASE WHEN i=1 THEN 'Kra 69a#93-110, Castilla' ELSE 'Carrera 74a #94-53, Castilla' END,1);
    END LOOP;
END $$;
COMMIT;
