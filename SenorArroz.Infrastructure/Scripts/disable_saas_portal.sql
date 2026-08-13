BEGIN;
INSERT INTO platform_setting(key, value_json, updated_at)
VALUES ('portal_enabled', 'false'::jsonb, now())
ON CONFLICT (key) DO UPDATE SET value_json = EXCLUDED.value_json, updated_at = now();
UPDATE tenant SET access_version = access_version + 1, updated_at = now() WHERE slug <> 'senor-arroz';
COMMIT;
