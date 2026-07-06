CREATE TABLE IF NOT EXISTS whatsapp_template (
    id serial PRIMARY KEY,
    branch_id integer NULL REFERENCES branch(id) ON DELETE CASCADE,
    business_account_id varchar(64) NULL,
    meta_template_id varchar(128) NOT NULL,
    name varchar(255) NOT NULL,
    language varchar(20) NOT NULL,
    category varchar(80) NOT NULL,
    status varchar(40) NOT NULL,
    components jsonb NOT NULL,
    created_at timestamp without time zone DEFAULT now(),
    updated_at timestamp without time zone DEFAULT now(),
    CONSTRAINT uq_whatsapp_template_meta_id UNIQUE (meta_template_id)
);

CREATE INDEX IF NOT EXISTS idx_whatsapp_template_account_name_language ON whatsapp_template(business_account_id, name, language);
CREATE INDEX IF NOT EXISTS idx_whatsapp_template_status ON whatsapp_template(status);
CREATE INDEX IF NOT EXISTS idx_whatsapp_template_branch ON whatsapp_template(branch_id);
