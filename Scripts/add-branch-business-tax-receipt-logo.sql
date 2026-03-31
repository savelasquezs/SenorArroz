-- Producción: nombre comercial, NIT (columna nit), logo en estáticos.
-- Si existía tax_id de una versión anterior, se renombra a nit.

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'branch' AND column_name = 'tax_id'
    ) AND NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'branch' AND column_name = 'nit'
    ) THEN
        ALTER TABLE branch RENAME COLUMN tax_id TO nit;
    END IF;
END $$;

ALTER TABLE branch ADD COLUMN IF NOT EXISTS business_name character varying(150);
ALTER TABLE branch ADD COLUMN IF NOT EXISTS nit character varying(32);
ALTER TABLE branch_print_settings ADD COLUMN IF NOT EXISTS receipt_logo_path character varying(500);
