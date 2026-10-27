BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '30s';

ALTER TABLE delivery_app_connection
    DROP CONSTRAINT IF EXISTS ux_delivery_app_connection_branch_provider;
DROP INDEX IF EXISTS ux_delivery_app_connection_branch_provider;

CREATE UNIQUE INDEX IF NOT EXISTS ux_delivery_app_connection_branch_provider_environment
    ON delivery_app_connection (branch_id, provider, environment);

COMMIT;
