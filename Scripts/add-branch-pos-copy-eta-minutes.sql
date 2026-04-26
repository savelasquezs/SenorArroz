-- Ventana de tiempo en «Copiar mensaje» (POS): minutos mínimos + rango (p. ej. 30 + 15 → 30-45 min).
-- Si existía la columna previa de texto libre, se elimina.

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'branch' AND column_name = 'pos_copy_message_eta_phrase'
    ) THEN
        ALTER TABLE branch DROP COLUMN pos_copy_message_eta_phrase;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'branch' AND column_name = 'pos_copy_eta_minutes'
    ) THEN
        ALTER TABLE branch
            ADD COLUMN pos_copy_eta_minutes integer NOT NULL DEFAULT 30;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'branch' AND column_name = 'pos_copy_eta_range_minutes'
    ) THEN
        ALTER TABLE branch
            ADD COLUMN pos_copy_eta_range_minutes integer NOT NULL DEFAULT 15;
    END IF;
END $$;
