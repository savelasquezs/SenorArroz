\set ON_ERROR_STOP on
\if :{?courier_count}
\else
\set courier_count 3
\endif
\if :{?age_minutes}
\else
\set age_minutes 20
\endif
DO $$ BEGIN
    IF current_database()<>'senor_arroz_delivery_dev' THEN RAISE EXCEPTION 'Solo base local de delivery'; END IF;
END $$;
SELECT set_config('app.system_scope','on',false);
BEGIN;
CREATE TEMP TABLE scenario_settings(couriers integer CHECK(couriers IN (3,5)),age integer CHECK(age BETWEEN 1 AND 180)) ON COMMIT DROP;
INSERT INTO scenario_settings VALUES (:'courier_count',:'age_minutes');
CREATE TEMP TABLE test_couriers ON COMMIT DROP AS
SELECT id,row_number() OVER (ORDER BY CASE email WHEN 'domiciliario@pruebas.local' THEN 0 ELSE 1 END,email) n
FROM "user" WHERE tenant_id=1 AND branch_id=1 AND email IN ('domiciliario@pruebas.local',
    'domiciliario.real1@pruebas.local','domiciliario.real2@pruebas.local',
    'domiciliario.real3@pruebas.local','domiciliario.real4@pruebas.local');
UPDATE "user" SET active=true WHERE id IN (SELECT id FROM test_couriers);
UPDATE "user" SET web_access_enabled=true WHERE tenant_id=1 AND email='domiciliario.real4@pruebas.local';
UPDATE delivery_work_session SET status='closed',ended_at=coalesce(ended_at,now()),
    end_reason='administrative_closure',updated_at=now()
WHERE tenant_id=1 AND deliveryman_id IN (SELECT id FROM test_couriers)
    AND (status<>'active' OR deliveryman_id IN (SELECT id FROM test_couriers WHERE n>(SELECT couriers FROM scenario_settings)));
INSERT INTO delivery_work_session(tenant_id,deliveryman_id,branch_id,device_installation_id,device_platform,
    device_description,app_version,started_at,auto_close_at,status,last_communication_at)
SELECT 1,c.id,1,'local-routing-'||c.id,'android','Prueba comparativa local','local-test',now(),now()+interval '8 hours','active',now()
FROM test_couriers c WHERE n<=(SELECT couriers FROM scenario_settings)
    AND NOT EXISTS(SELECT 1 FROM delivery_work_session s WHERE s.deliveryman_id=c.id AND s.status='active');
UPDATE delivery_work_session SET last_communication_at=now(),auto_close_at=now()+interval '8 hours',updated_at=now()
WHERE tenant_id=1 AND status='active' AND deliveryman_id IN (SELECT id FROM test_couriers WHERE n<=(SELECT couriers FROM scenario_settings));
INSERT INTO deliveryman_location(tenant_id,deliveryman_id,work_session_id,latitude,longitude,recorded_at,synced_at,
    accuracy_meters,gps_enabled,internet_available,tracking_mode)
SELECT 1,s.deliveryman_id,s.id,b.latitude,b.longitude,now(),now(),8,true,true,'light'
FROM delivery_work_session s JOIN branch b ON b.id=s.branch_id AND b.tenant_id=s.tenant_id
WHERE s.tenant_id=1 AND s.status='active' AND s.deliveryman_id IN (SELECT id FROM test_couriers WHERE n<=(SELECT couriers FROM scenario_settings));
UPDATE "order" SET status='ready',created_at=now()-make_interval(mins=>cfg.age),updated_at=now(),
    status_times=jsonb_build_object('taken',now()-make_interval(mins=>cfg.age),
        'inpreparation',now()-make_interval(mins=>greatest(1,cfg.age-5)), 'ready',now()-interval '1 minute')
FROM scenario_settings cfg WHERE tenant_id=1 AND branch_id=1 AND notes LIKE 'Prueba coordenadas 2026-10-18%'
    AND delivery_man_id IS NULL AND delivery_route_id IS NULL;
COMMIT;
