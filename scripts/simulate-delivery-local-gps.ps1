param([ValidateRange(1,480)][int]$DurationMinutes=120)
$ErrorActionPreference='Stop'
$deadline=[DateTime]::UtcNow.AddMinutes($DurationMinutes)
$sql=@'
\set ON_ERROR_STOP on
DO $$ BEGIN
    IF current_database()<>'senor_arroz_delivery_dev' THEN RAISE EXCEPTION 'Base incorrecta'; END IF;
END $$;
SELECT set_config('app.system_scope','on',false);
BEGIN;
INSERT INTO deliveryman_location(tenant_id,deliveryman_id,work_session_id,latitude,longitude,recorded_at,synced_at,
    accuracy_meters,gps_enabled,internet_available,tracking_mode)
SELECT u.tenant_id,u.id,s.id,coalesce(p.latitude,b.latitude),coalesce(p.longitude,b.longitude),now(),now(),8,true,true,'light'
FROM "user" u JOIN delivery_work_session s ON s.deliveryman_id=u.id AND s.tenant_id=u.tenant_id
    AND s.status='active' AND s.auto_close_at>now()
JOIN branch b ON b.id=s.branch_id AND b.tenant_id=u.tenant_id
LEFT JOIN LATERAL (SELECT latitude,longitude FROM deliveryman_location l WHERE l.tenant_id=u.tenant_id
    AND l.work_session_id=s.id ORDER BY recorded_at DESC,id DESC LIMIT 1) p ON true
WHERE u.tenant_id=1 AND u.active
    AND u.email IN ('domiciliario@pruebas.local','domiciliario.real1@pruebas.local','domiciliario.real2@pruebas.local',
        'domiciliario.real3@pruebas.local','domiciliario.real4@pruebas.local')
    AND NOT EXISTS(SELECT 1 FROM "order" o WHERE o.tenant_id=u.tenant_id AND o.delivery_man_id=u.id
        AND o.status NOT IN ('delivered','cancelled'))
    AND NOT EXISTS(SELECT 1 FROM delivery_route r WHERE r.tenant_id=u.tenant_id AND r.deliveryman_id=u.id
        AND r.status IN ('open','in_progress'));
UPDATE delivery_work_session s SET last_communication_at=now(),updated_at=now()
FROM "user" u WHERE u.id=s.deliveryman_id AND u.tenant_id=s.tenant_id
    AND u.email IN ('domiciliario@pruebas.local','domiciliario.real1@pruebas.local','domiciliario.real2@pruebas.local',
        'domiciliario.real3@pruebas.local','domiciliario.real4@pruebas.local')
    AND s.status='active' AND s.tenant_id=1 AND EXISTS(SELECT 1 FROM deliveryman_location l
    WHERE l.work_session_id=s.id AND l.recorded_at>=now()-interval '5 seconds');
COMMIT;
'@
while([DateTime]::UtcNow -lt $deadline) {
    $sql | docker exec -i senorarroz-delivery-postgres psql -U delivery_owner -d senor_arroz_delivery_dev
    if($LASTEXITCODE -ne 0){throw 'Falló la simulación GPS local.'}
    Start-Sleep -Seconds 60
}
