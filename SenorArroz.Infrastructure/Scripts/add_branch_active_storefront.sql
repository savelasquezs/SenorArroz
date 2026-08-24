ALTER TABLE branch
    ADD COLUMN IF NOT EXISTS is_active boolean NOT NULL DEFAULT true;

CREATE INDEX IF NOT EXISTS ix_branch_is_active
    ON branch (is_active)
    WHERE is_active = true;

