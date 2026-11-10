SET LOCAL lock_timeout='500ms';
SET LOCAL statement_timeout='15000ms';
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'gateway_oauth') THEN
        CREATE SCHEMA gateway_oauth;
    END IF;
END $EF$;


CREATE TABLE IF NOT EXISTS gateway_oauth.application (
    id text NOT NULL,
    tenant_id integer NOT NULL DEFAULT 1,
    application_type character varying(50),
    client_id character varying(100),
    client_secret text,
    client_type character varying(50),
    concurrency_token character varying(50),
    consent_type character varying(50),
    display_name text,
    display_names text,
    json_web_key_set text,
    permissions text,
    post_logout_redirect_uris text,
    properties text,
    redirect_uris text,
    requirements text,
    settings text,
    CONSTRAINT "PK_application" PRIMARY KEY (id)
);


CREATE TABLE IF NOT EXISTS gateway_oauth.scope (
    id text NOT NULL,
    tenant_id integer NOT NULL DEFAULT 1,
    concurrency_token character varying(50),
    description text,
    descriptions text,
    display_name text,
    display_names text,
    name character varying(200),
    properties text,
    resources text,
    CONSTRAINT "PK_scope" PRIMARY KEY (id)
);


CREATE TABLE IF NOT EXISTS gateway_oauth."authorization" (
    id text NOT NULL,
    tenant_id integer NOT NULL DEFAULT 1,
    application_id text,
    concurrency_token character varying(50),
    creation_date timestamp with time zone,
    properties text,
    scopes text,
    status character varying(50),
    subject character varying(400),
    type character varying(50),
    CONSTRAINT "PK_authorization" PRIMARY KEY (id),
    CONSTRAINT "FK_authorization_application_application_id" FOREIGN KEY (application_id) REFERENCES gateway_oauth.application (id)
);


CREATE TABLE IF NOT EXISTS gateway_oauth.token (
    id text NOT NULL,
    tenant_id integer NOT NULL DEFAULT 1,
    application_id text,
    authorization_id text,
    concurrency_token character varying(50),
    creation_date timestamp with time zone,
    expiration_date timestamp with time zone,
    payload text,
    properties text,
    redemption_date timestamp with time zone,
    reference_id character varying(100),
    status character varying(50),
    subject character varying(400),
    type character varying(150),
    CONSTRAINT "PK_token" PRIMARY KEY (id),
    CONSTRAINT "FK_token_application_application_id" FOREIGN KEY (application_id) REFERENCES gateway_oauth.application (id),
    CONSTRAINT "FK_token_authorization_authorization_id" FOREIGN KEY (authorization_id) REFERENCES gateway_oauth."authorization" (id)
);


CREATE UNIQUE INDEX IF NOT EXISTS "IX_application_client_id" ON gateway_oauth.application (client_id);


CREATE INDEX IF NOT EXISTS "IX_authorization_application_id_status_subject_type" ON gateway_oauth."authorization" (application_id, status, subject, type);


CREATE UNIQUE INDEX IF NOT EXISTS "IX_scope_name" ON gateway_oauth.scope (name);


CREATE INDEX IF NOT EXISTS "IX_token_application_id_status_subject_type" ON gateway_oauth.token (application_id, status, subject, type);


CREATE INDEX IF NOT EXISTS "IX_token_authorization_id" ON gateway_oauth.token (authorization_id);


CREATE UNIQUE INDEX IF NOT EXISTS "IX_token_reference_id" ON gateway_oauth.token (reference_id);



REVOKE ALL ON SCHEMA gateway_oauth FROM PUBLIC;
REVOKE ALL ON ALL TABLES IN SCHEMA gateway_oauth FROM PUBLIC;
DO $gateway$
DECLARE t text;
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='senorarroz_runtime' AND NOT rolsuper AND NOT rolbypassrls) THEN
    RAISE EXCEPTION 'Expected restricted API runtime role is missing or unsafe';
  END IF;
  GRANT USAGE ON SCHEMA gateway_oauth TO senorarroz_runtime;
  FOREACH t IN ARRAY ARRAY['application','authorization','scope','token'] LOOP
    EXECUTE format('ALTER TABLE gateway_oauth.%I ENABLE ROW LEVEL SECURITY',t);
    EXECUTE format('ALTER TABLE gateway_oauth.%I FORCE ROW LEVEL SECURITY',t);
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname='gateway_oauth' AND tablename=t AND policyname='gateway_oauth_tenant_1') THEN
      EXECUTE format('CREATE POLICY gateway_oauth_tenant_1 ON gateway_oauth.%I FOR ALL TO senorarroz_runtime USING (tenant_id=1) WITH CHECK (tenant_id=1)',t);
    END IF;
    EXECUTE format('DROP POLICY IF EXISTS gateway_oauth_tenant_guard ON gateway_oauth.%I',t);
    EXECUTE format('CREATE POLICY gateway_oauth_tenant_guard ON gateway_oauth.%I AS RESTRICTIVE FOR ALL TO senorarroz_runtime USING (tenant_id=1) WITH CHECK (tenant_id=1)',t);
    EXECUTE format('GRANT SELECT,INSERT,UPDATE,DELETE ON gateway_oauth.%I TO senorarroz_runtime',t);
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='senorarroz_db_reader') THEN
      EXECUTE format('REVOKE ALL ON gateway_oauth.%I FROM senorarroz_db_reader',t);
    END IF;
  END LOOP;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='senorarroz_db_reader') THEN
    REVOKE ALL ON SCHEMA gateway_oauth FROM senorarroz_db_reader;
  END IF;
END $gateway$;
