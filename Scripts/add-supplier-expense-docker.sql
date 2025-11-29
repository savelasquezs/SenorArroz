DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_name = 'supplier'
          AND column_name = 'branch_id'
    ) THEN
        ALTER TABLE supplier
        ADD COLUMN branch_id integer NOT NULL DEFAULT 0;
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS supplier_expense (
    id SERIAL PRIMARY KEY,
    supplier_id integer NOT NULL,
    expense_id integer NOT NULL,
    usage_count integer NOT NULL DEFAULT 0,
    last_used_at timestamptz NULL,
    last_unit_price numeric(18,2) NULL,
    created_at timestamptz NOT NULL DEFAULT NOW(),
    updated_at timestamptz NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_supplier_branch_name ON supplier (branch_id, name);
CREATE INDEX IF NOT EXISTS "IX_supplier_expense_expense_id" ON supplier_expense (expense_id);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_supplier_expense_supplier_id_expense_id" ON supplier_expense (supplier_id, expense_id);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_supplier_expense_expense_expense_id'
    ) THEN
        ALTER TABLE supplier_expense
        ADD CONSTRAINT "FK_supplier_expense_expense_expense_id"
        FOREIGN KEY (expense_id) REFERENCES expense(id) ON DELETE CASCADE;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_supplier_expense_supplier_supplier_id'
    ) THEN
        ALTER TABLE supplier_expense
        ADD CONSTRAINT "FK_supplier_expense_supplier_supplier_id"
        FOREIGN KEY (supplier_id) REFERENCES supplier(id) ON DELETE CASCADE;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_supplier_branch_branch_id'
    ) THEN
        ALTER TABLE supplier
        ADD CONSTRAINT "FK_supplier_branch_branch_id"
        FOREIGN KEY (branch_id) REFERENCES branch(id) ON DELETE RESTRICT;
    END IF;
END $$;

