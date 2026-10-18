\set ON_ERROR_STOP on
DO $$ BEGIN
    IF current_database() <> 'senor_arroz_delivery_dev' THEN
        RAISE EXCEPTION 'Solo se permite la base local de delivery_app';
    END IF;
END $$;
SELECT set_config('app.system_scope', 'on', false);
BEGIN;
UPDATE "order" SET created_at=now()-interval '12 minutes',
    status_times=jsonb_build_object('taken',now()-interval '12 minutes',
        'inpreparation',now()-interval '10 minutes','ready',now()-interval '1 minute'),updated_at=now()
WHERE tenant_id=1 AND notes LIKE 'Prueba real%' AND status='ready'
    AND delivery_man_id IS NULL AND delivery_route_id IS NULL;
WITH preparing AS (
    SELECT id,row_number() OVER (ORDER BY id) n FROM "order"
    WHERE tenant_id=1 AND notes LIKE 'Prueba real%' AND status='in_preparation'
        AND delivery_man_id IS NULL AND delivery_route_id IS NULL
)
UPDATE "order" o SET created_at=now()-CASE WHEN p.n<=3 THEN interval '25 minutes' ELSE interval '8 minutes' END,
    status_times=jsonb_build_object('taken',now()-CASE WHEN p.n<=3 THEN interval '25 minutes' ELSE interval '8 minutes' END,
        'inpreparation',now()-CASE WHEN p.n<=3 THEN interval '20 minutes' ELSE interval '5 minutes' END),updated_at=now()
FROM preparing p WHERE o.id=p.id;
INSERT INTO delivery_work_session(tenant_id,deliveryman_id,branch_id,device_installation_id,device_platform,
    device_description,app_version,started_at,auto_close_at,status,last_communication_at)
SELECT u.tenant_id,u.id,u.branch_id,'local-routing-'||u.id,'android','Simulación local de rutas','local-test',
    now(),now()+interval '8 hours','active',now()
FROM "user" u WHERE u.tenant_id=1 AND u.active
    AND u.email IN ('domiciliario@pruebas.local','domiciliario.real1@pruebas.local',
        'domiciliario.real2@pruebas.local','domiciliario.real3@pruebas.local')
    AND NOT EXISTS(SELECT 1 FROM delivery_work_session s WHERE s.deliveryman_id=u.id AND s.status='active');
INSERT INTO deliveryman_location(tenant_id,deliveryman_id,work_session_id,latitude,longitude,recorded_at,synced_at,
    accuracy_meters,gps_enabled,internet_available,tracking_mode)
SELECT u.tenant_id,u.id,s.id,b.latitude,b.longitude,now(),now(),8,true,true,'light'
FROM "user" u JOIN delivery_work_session s ON s.deliveryman_id=u.id AND s.tenant_id=u.tenant_id
    AND s.status='active' AND s.auto_close_at>now()
JOIN branch b ON b.id=s.branch_id AND b.tenant_id=u.tenant_id
WHERE u.tenant_id=1 AND u.active
    AND u.email IN ('domiciliario@pruebas.local','domiciliario.real1@pruebas.local',
        'domiciliario.real2@pruebas.local','domiciliario.real3@pruebas.local');
UPDATE delivery_work_session s SET last_communication_at=now(),updated_at=now()
FROM "user" u WHERE u.id=s.deliveryman_id AND s.tenant_id=1 AND s.status='active'
    AND u.email IN ('domiciliario@pruebas.local','domiciliario.real1@pruebas.local',
        'domiciliario.real2@pruebas.local','domiciliario.real3@pruebas.local');
COMMIT;
