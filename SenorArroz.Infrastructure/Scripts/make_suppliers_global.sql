ALTER TABLE supplier ALTER COLUMN branch_id DROP NOT NULL;

DROP INDEX IF EXISTS idx_supplier_branch_name;
CREATE INDEX IF NOT EXISTS idx_supplier_name ON supplier(name);
CREATE INDEX IF NOT EXISTS idx_supplier_phone ON supplier(phone);
