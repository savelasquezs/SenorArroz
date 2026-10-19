\set ON_ERROR_STOP on
SELECT set_config('app.system_scope','on',false);
UPDATE branch
SET delivery_tracking_allowed_distance_meters=400, updated_at=now()
WHERE tenant_id=1 AND id=1;
