-- Script para agregar created_by_id a expense_header
-- Basado en la migración: AddCreatedByIdToExpenseHeader
-- Ejecutar después de verificar que las migraciones anteriores estén aplicadas

BEGIN;

-- Verificar si la migración ya se aplicó (idempotente)
DO $migration$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM "__EFMigrationsHistory" 
        WHERE "MigrationId" = '20251128025240_AddCreatedByIdToExpenseHeader'
    ) THEN
        -- Agregar columna como nullable primero
        IF NOT EXISTS (
            SELECT 1 FROM information_schema.columns 
            WHERE table_schema = 'public' 
            AND table_name = 'expense_header' 
            AND column_name = 'created_by_id'
        ) THEN
            ALTER TABLE expense_header 
            ADD COLUMN created_by_id INTEGER NULL;
        END IF;

        -- Actualizar registros existentes: asignar el primer admin de cada sucursal
        UPDATE expense_header eh
        SET created_by_id = (
            SELECT u.id
            FROM "user" u
            WHERE u.branch_id = eh.branch_id
                AND u.role IN ('admin', 'superadmin')
                AND u.active = true
            ORDER BY u.id
            LIMIT 1
        )
        WHERE eh.created_by_id IS NULL;

        -- Si aún hay registros sin created_by_id, asignar el primer usuario de la sucursal
        UPDATE expense_header eh
        SET created_by_id = (
            SELECT u.id
            FROM "user" u
            WHERE u.branch_id = eh.branch_id
                AND u.active = true
            ORDER BY u.id
            LIMIT 1
        )
        WHERE eh.created_by_id IS NULL;

        -- Hacer la columna NOT NULL
        ALTER TABLE expense_header 
        ALTER COLUMN created_by_id SET NOT NULL;

        -- Crear índice si no existe
        IF NOT EXISTS (
            SELECT 1 FROM pg_indexes 
            WHERE schemaname = 'public' 
            AND tablename = 'expense_header' 
            AND indexname = 'idx_expense_header_created_by'
        ) THEN
            CREATE INDEX idx_expense_header_created_by 
            ON expense_header(created_by_id);
        END IF;

        -- Agregar Foreign Key si no existe
        IF NOT EXISTS (
            SELECT 1 FROM information_schema.table_constraints 
            WHERE constraint_schema = 'public' 
            AND table_name = 'expense_header' 
            AND constraint_name = 'FK_expense_header_user_created_by_id'
        ) THEN
            ALTER TABLE expense_header
            ADD CONSTRAINT FK_expense_header_user_created_by_id
            FOREIGN KEY (created_by_id)
            REFERENCES "user"(id)
            ON DELETE RESTRICT;
        END IF;

        -- Registrar la migración
        INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
        VALUES ('20251128025240_AddCreatedByIdToExpenseHeader', '9.0.0')
        ON CONFLICT ("MigrationId") DO NOTHING;
    END IF;
END
$migration$;

COMMIT;

