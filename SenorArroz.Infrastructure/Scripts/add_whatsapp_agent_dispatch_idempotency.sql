BEGIN;
ALTER TABLE whatsapp_message ADD COLUMN IF NOT EXISTS agent_dispatch_key varchar(180) NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_whatsapp_message_agent_dispatch_key ON whatsapp_message(agent_dispatch_key) WHERE agent_dispatch_key IS NOT NULL;
COMMIT;
