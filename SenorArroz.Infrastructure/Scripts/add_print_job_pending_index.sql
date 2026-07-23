-- Ejecutar fuera de una transacción para no bloquear escrituras mientras se crea.
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_print_job_pending_branch_kind_created
    ON print_job (branch_id, kind, created_at, id)
    WHERE status = 'pending';
