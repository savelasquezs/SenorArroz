-- ==============================================================================
-- Script: Agregar columna verified_at y trigger automático a bank_payment
-- Propósito: Mantener sincronizado is_verified (boolean) con verified_at (timestamp)
-- Fecha: Octubre 2024
-- ==============================================================================

-- 1. Agregar columna verified_at si no existe
ALTER TABLE bank_payment 
ADD COLUMN IF NOT EXISTS verified_at TIMESTAMP WITH TIME ZONE;

-- 2. Crear función del trigger para manejar verified_at automáticamente
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

-- 3. Eliminar trigger anterior si existe
DROP TRIGGER IF EXISTS trigger_bank_payment_verified_at ON bank_payment;

-- 4. Crear trigger que se ejecuta ANTES de cada UPDATE
CREATE TRIGGER trigger_bank_payment_verified_at
    BEFORE UPDATE ON bank_payment
    FOR EACH ROW
    EXECUTE FUNCTION update_bank_payment_verified_at();

-- 5. Actualizar verified_at para registros existentes donde is_verified = true
-- (Usar created_at como fecha de verificación para registros antiguos)
UPDATE bank_payment 
SET verified_at = created_at 
WHERE is_verified = true AND verified_at IS NULL;

-- 6. Verificar que los cambios se aplicaron correctamente
SELECT 
    column_name, 
    data_type, 
    is_nullable,
    column_default
FROM information_schema.columns 
WHERE table_name = 'bank_payment' 
  AND column_name IN ('is_verified', 'verified_at')
ORDER BY ordinal_position;

-- 7. Verificar que el trigger se creó correctamente
SELECT 
    trigger_name,
    event_manipulation,
    event_object_table,
    action_timing
FROM information_schema.triggers
WHERE trigger_name = 'trigger_bank_payment_verified_at';

-- ==============================================================================
-- Resultado esperado:
-- - Columna verified_at (timestamp with time zone, nullable)
-- - Trigger activo en bank_payment
-- - Registros existentes con verified_at actualizado
-- ==============================================================================

