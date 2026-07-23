-- Rollback: ejecutar fuera de una transacción.
DROP INDEX CONCURRENTLY IF EXISTS ix_print_job_pending_branch_kind_created;
