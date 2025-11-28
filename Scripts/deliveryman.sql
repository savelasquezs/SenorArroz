-- Script para agregar la tabla deliveryman_advance a Railway
-- Basado en la migración: AddDeliverymanAdvanceTable
-- Ejecutar después de verificar que las migraciones anteriores estén aplicadas

BEGIN;

-- Verificar si la tabla ya existe (idempotente)
DO $migration$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.tables 
        WHERE table_schema = 'public' 
        AND table_name = 'deliveryman_advance'
    ) THEN
        -- Crear la tabla
        CREATE TABLE deliveryman_advance (
            id SERIAL PRIMARY KEY,
            deliveryman_id INTEGER NOT NULL,
            amount DECIMAL(10,2) NOT NULL CHECK (amount > 0),
            notes TEXT,
            created_by INTEGER NOT NULL,
            branch_id INTEGER NOT NULL,
            created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
            
            -- Foreign Keys
            CONSTRAINT FK_deliveryman_advance_user_deliveryman_id 
                FOREIGN KEY (deliveryman_id) 
                REFERENCES "user"(id) 
                ON DELETE RESTRICT,
            
            CONSTRAINT FK_deliveryman_advance_user_created_by 
                FOREIGN KEY (created_by) 
                REFERENCES "user"(id) 
                ON DELETE RESTRICT,
            
            CONSTRAINT FK_deliveryman_advance_branch_branch_id 
                FOREIGN KEY (branch_id) 
                REFERENCES branch(id) 
                ON DELETE RESTRICT
        );

        -- Crear índices
        CREATE INDEX idx_deliveryman_advance_deliveryman ON deliveryman_advance(deliveryman_id);
        CREATE INDEX idx_deliveryman_advance_created_at ON deliveryman_advance(created_at);
        CREATE INDEX idx_deliveryman_advance_branch ON deliveryman_advance(branch_id);
        CREATE INDEX idx_deliveryman_advance_created_by ON deliveryman_advance(created_by);

        -- Crear función para actualizar updated_at
        CREATE OR REPLACE FUNCTION update_deliveryman_advance_updated_at()
        RETURNS TRIGGER AS $$
        BEGIN
            NEW.updated_at = NOW();
            RETURN NEW;
        END;
        $$ LANGUAGE plpgsql;

        -- Crear trigger
        CREATE TRIGGER trigger_deliveryman_advance_updated_at
            BEFORE UPDATE ON deliveryman_advance
            FOR EACH ROW
            EXECUTE FUNCTION update_deliveryman_advance_updated_at();

        -- Agregar comentarios
        COMMENT ON TABLE deliveryman_advance IS 'Tabla para registrar abonos/adelantos realizados a domiciliarios';
        COMMENT ON COLUMN deliveryman_advance.deliveryman_id IS 'ID del domiciliario que recibe el abono';
        COMMENT ON COLUMN deliveryman_advance.amount IS 'Monto del abono en pesos colombianos';
        COMMENT ON COLUMN deliveryman_advance.notes IS 'Comentarios o notas adicionales sobre el abono';
        COMMENT ON COLUMN deliveryman_advance.created_by IS 'ID del usuario (admin/cajero) que creó el abono';
        COMMENT ON COLUMN deliveryman_advance.branch_id IS 'ID de la sucursal donde se realizó el abono';

        -- Registrar la migración en el historial de EF Core
        INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
        VALUES ('20251128005639_AddDeliverymanAdvanceTable', '9.0.8')
        ON CONFLICT ("MigrationId") DO NOTHING;

        RAISE NOTICE 'Tabla deliveryman_advance creada exitosamente';
    ELSE
        RAISE NOTICE 'La tabla deliveryman_advance ya existe, omitiendo creación';
    END IF;
END $migration$;

COMMIT;