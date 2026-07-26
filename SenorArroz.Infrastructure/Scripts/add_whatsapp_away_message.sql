ALTER TABLE whatsapp_branch_setting
    ADD COLUMN IF NOT EXISTS away_message_enabled boolean NOT NULL DEFAULT false;

ALTER TABLE whatsapp_branch_setting
    ADD COLUMN IF NOT EXISTS away_message_text varchar(3500) NULL;

COMMENT ON COLUMN whatsapp_branch_setting.away_message_enabled IS
    'Envia un aviso automatico una vez por conversacion y periodo de cierre.';

COMMENT ON COLUMN whatsapp_branch_setting.away_message_text IS
    'Plantilla por sucursal. Variables admitidas: {{BranchName}} y {{NextOpening}}.';
