-- ==============================================================================
-- Script: Sincronizar Esquema de Base de Datos con Entity Framework
-- Propósito: Agregar columnas y tablas faltantes para que el esquema coincida
--            con las configuraciones de Entity Framework Core
-- Fecha: Noviembre 2024
-- ==============================================================================

-- Este script es IDEMPOTENTE: puede ejecutarse múltiples veces sin errores
-- Usa IF NOT EXISTS y ON CONFLICT para evitar errores en ejecuciones repetidas

BEGIN;

-- ==============================================================================
-- 1. AGREGAR COLUMNA is_primary A TABLA address
-- ==============================================================================

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 
        FROM information_schema.columns 
        WHERE table_name = 'address' 
        AND column_name = 'is_primary'
    ) THEN
        ALTER TABLE address 
        ADD COLUMN is_primary BOOLEAN NOT NULL DEFAULT false;
        
        RAISE NOTICE 'Columna is_primary agregada a tabla address';
    ELSE
        RAISE NOTICE 'Columna is_primary ya existe en tabla address';
    END IF;
END $$;

-- ==============================================================================
-- 2. AGREGAR COLUMNA guestname A TABLA order
-- ==============================================================================

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 
        FROM information_schema.columns 
        WHERE table_name = 'order' 
        AND column_name = 'guestname'
    ) THEN
        ALTER TABLE "order" 
        ADD COLUMN guestname VARCHAR(100);
        
        RAISE NOTICE 'Columna guestname agregada a tabla order';
    ELSE
        RAISE NOTICE 'Columna guestname ya existe en tabla order';
    END IF;
END $$;

-- ==============================================================================
-- 3. AGREGAR COLUMNA verified_at A TABLA bank_payment
-- ==============================================================================
-- Nota: Este script incluye la lógica del script add_verified_at_column_and_trigger.sql
-- para mantener todo en un solo lugar

-- 3.1. Agregar columna verified_at si no existe
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 
        FROM information_schema.columns 
        WHERE table_name = 'bank_payment' 
        AND column_name = 'verified_at'
    ) THEN
        ALTER TABLE bank_payment 
        ADD COLUMN verified_at TIMESTAMP WITH TIME ZONE;
        
        RAISE NOTICE 'Columna verified_at agregada a tabla bank_payment';
    ELSE
        RAISE NOTICE 'Columna verified_at ya existe en tabla bank_payment';
    END IF;
END $$;

-- 3.2. Crear función del trigger para manejar verified_at automáticamente
CREATE OR REPLACE FUNCTION update_bank_payment_verified_at()
RETURNS TRIGGER AS $$
BEGIN
    -- Cuando se verifica el pago (is_verified cambia de false a true)
    IF NEW.is_verified = true AND (OLD.is_verified = false OR OLD.is_verified IS NULL) THEN
        NEW.verified_at := NOW();
    -- Cuando se desverifica el pago (is_verified cambia de true a false)
    ELSIF NEW.is_verified = false AND OLD.is_verified = true THEN
        NEW.verified_at := NULL;
    END IF;
    
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- 3.3. Eliminar trigger anterior si existe y crear nuevo
DROP TRIGGER IF EXISTS trigger_bank_payment_verified_at ON bank_payment;

CREATE TRIGGER trigger_bank_payment_verified_at
    BEFORE UPDATE ON bank_payment
    FOR EACH ROW
    EXECUTE FUNCTION update_bank_payment_verified_at();

-- 3.4. Actualizar verified_at para registros existentes donde is_verified = true
UPDATE bank_payment 
SET verified_at = created_at 
WHERE is_verified = true AND verified_at IS NULL;

-- ==============================================================================
-- 4. CREAR TABLA password_reset_token
-- ==============================================================================

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 
        FROM information_schema.tables 
        WHERE table_name = 'password_reset_token'
    ) THEN
        -- Crear tabla
        CREATE TABLE password_reset_token (
            id SERIAL PRIMARY KEY,
            user_id INTEGER NOT NULL,
            token VARCHAR(500) NOT NULL,
            email VARCHAR(100) NOT NULL,
            expires_at TIMESTAMP NOT NULL,
            is_used BOOLEAN NOT NULL DEFAULT false,
            used_at TIMESTAMP,
            used_by_ip VARCHAR(45),
            created_at TIMESTAMP DEFAULT NOW(),
            updated_at TIMESTAMP DEFAULT NOW(),
            CONSTRAINT fk_password_reset_token_user 
                FOREIGN KEY (user_id) 
                REFERENCES "user"(id) 
                ON DELETE CASCADE
        );

        -- Crear índices
        CREATE UNIQUE INDEX idx_password_reset_token_token 
            ON password_reset_token(token);
        
        CREATE INDEX idx_password_reset_token_user_id 
            ON password_reset_token(user_id);
        
        CREATE INDEX idx_password_reset_token_email 
            ON password_reset_token(email);
        
        CREATE INDEX idx_password_reset_token_expires_at 
            ON password_reset_token(expires_at);
        
        CREATE INDEX idx_password_reset_token_user_used 
            ON password_reset_token(user_id, is_used);

        -- Crear trigger para updated_at
        CREATE TRIGGER update_password_reset_token_updated_at 
            BEFORE UPDATE ON password_reset_token 
            FOR EACH ROW 
            EXECUTE FUNCTION update_updated_at_column();

        RAISE NOTICE 'Tabla password_reset_token creada con todos sus índices';
    ELSE
        RAISE NOTICE 'Tabla password_reset_token ya existe';
    END IF;
END $$;

-- ==============================================================================
-- VERIFICACIÓN FINAL
-- ==============================================================================

DO $$
DECLARE
    missing_columns TEXT[] := ARRAY[]::TEXT[];
BEGIN
    -- Verificar is_primary en address
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'address' AND column_name = 'is_primary'
    ) THEN
        missing_columns := array_append(missing_columns, 'address.is_primary');
    END IF;

    -- Verificar guestname en order
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'order' AND column_name = 'guestname'
    ) THEN
        missing_columns := array_append(missing_columns, 'order.guestname');
    END IF;

    -- Verificar verified_at en bank_payment
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'bank_payment' AND column_name = 'verified_at'
    ) THEN
        missing_columns := array_append(missing_columns, 'bank_payment.verified_at');
    END IF;

    -- Verificar password_reset_token existe
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.tables 
        WHERE table_name = 'password_reset_token'
    ) THEN
        missing_columns := array_append(missing_columns, 'password_reset_token (tabla completa)');
    END IF;

    IF array_length(missing_columns, 1) > 0 THEN
        RAISE WARNING 'Algunas columnas/tablas aún faltan: %', array_to_string(missing_columns, ', ');
    ELSE
        RAISE NOTICE 'Sincronización completada exitosamente. Todas las columnas y tablas están presentes.';
    END IF;
END $$;

COMMIT;

-- ==============================================================================
-- Resultado esperado:
-- - Columna is_primary en address (boolean, default false)
-- - Columna guestname en order (varchar(100), nullable)
-- - Columna verified_at en bank_payment (timestamp, nullable)
-- - Tabla password_reset_token completa con todos sus índices
-- - Trigger para verified_at funcionando
-- ==============================================================================

