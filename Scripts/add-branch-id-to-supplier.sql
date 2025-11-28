-- Script: add-branch-id-to-supplier.sql
-- Objetivo: convertir a los proveedores en entidades por sucursal (branch_id)
-- Este script es idempotente para que pueda ejecutarse múltiples veces sin errores.

BEGIN;

-- 1. Agregar columna branch_id si aún no existe
ALTER TABLE IF EXISTS supplier
    ADD COLUMN IF NOT EXISTS branch_id integer;

-- 2. Asignar branch_id usando los expense_header existentes
WITH supplier_branch AS (
    SELECT supplier_id, MIN(branch_id) AS branch_id
    FROM expense_header
    GROUP BY supplier_id
)
UPDATE supplier s
SET branch_id = sb.branch_id
FROM supplier_branch sb
WHERE s.id = sb.supplier_id
  AND (s.branch_id IS NULL OR s.branch_id <> sb.branch_id);

-- 3. Para proveedores sin relación en expense_header, usar la primera sucursal disponible
DO $$
DECLARE
    first_branch_id integer;
BEGIN
    SELECT id INTO first_branch_id
    FROM branch
    ORDER BY id
    LIMIT 1;

    IF first_branch_id IS NOT NULL THEN
        UPDATE supplier
        SET branch_id = first_branch_id
        WHERE branch_id IS NULL;
    END IF;
END $$;

-- 4. Asegurar que la columna sea NOT NULL
ALTER TABLE supplier
    ALTER COLUMN branch_id SET NOT NULL;

-- 5. Crear índice compuesto si no existe
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = 'idx_supplier_branch_name'
          AND n.nspname = 'public'
    ) THEN
        CREATE INDEX idx_supplier_branch_name ON supplier (branch_id, name);
    END IF;
END $$;

-- 6. Agregar la restricción FK si no existe
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_supplier_branch_branch_id'
    ) THEN
        ALTER TABLE supplier
            ADD CONSTRAINT "FK_supplier_branch_branch_id"
            FOREIGN KEY (branch_id) REFERENCES branch (id) ON DELETE RESTRICT;
    END IF;
END $$;

COMMIT;

