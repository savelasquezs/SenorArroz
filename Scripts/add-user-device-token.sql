-- ─────────────────────────────────────────────────────────────────────────────
-- Parche de producción: tabla user_device_token (FCM push notifications)
-- Correr UNA sola vez en producción.
-- ─────────────────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS user_device_token (
    id              SERIAL PRIMARY KEY,
    user_id         INTEGER NOT NULL REFERENCES "user"(id) ON DELETE CASCADE,
    token           VARCHAR(512) NOT NULL,
    platform        VARCHAR(20) NOT NULL DEFAULT 'android',
    last_seen_at    TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    created_at      TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

-- Índices
CREATE UNIQUE INDEX IF NOT EXISTS uq_user_device_token_token
    ON user_device_token(token);

CREATE INDEX IF NOT EXISTS idx_user_device_token_user
    ON user_device_token(user_id);

-- Trigger updated_at (mismo patrón que el resto de tablas)
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_trigger
        WHERE tgname = 'trg_user_device_token_updated_at'
    ) THEN
        CREATE TRIGGER trg_user_device_token_updated_at
            BEFORE UPDATE ON user_device_token
            FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
    END IF;
END;
$$;
