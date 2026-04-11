-- Movimientos banco ↔ efectivo: permite from_bank_id o to_bank_id NULL (extremo "efectivo de caja").
-- Ejecutar en producción una vez.

ALTER TABLE bank_transfer ALTER COLUMN from_bank_id DROP NOT NULL;
ALTER TABLE bank_transfer ALTER COLUMN to_bank_id DROP NOT NULL;

ALTER TABLE bank_transfer DROP CONSTRAINT IF EXISTS chk_bank_transfer_endpoints;
ALTER TABLE bank_transfer ADD CONSTRAINT chk_bank_transfer_endpoints CHECK (
    (from_bank_id IS NOT NULL OR to_bank_id IS NOT NULL)
    AND (from_bank_id IS NULL OR to_bank_id IS NULL OR from_bank_id <> to_bank_id)
);
