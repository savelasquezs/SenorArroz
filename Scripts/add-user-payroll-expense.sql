-- Producción: seguimiento de nómina por ítem de catálogo asignado al usuario.
-- Ejecutar una vez contra la base existente.

ALTER TABLE "user" ADD COLUMN IF NOT EXISTS payroll_expense_id integer;

ALTER TABLE "user" DROP CONSTRAINT IF EXISTS "FK_user_expense_payroll_expense_id";
ALTER TABLE "user" ADD CONSTRAINT "FK_user_expense_payroll_expense_id"
    FOREIGN KEY (payroll_expense_id) REFERENCES expense (id) ON DELETE SET NULL;

CREATE UNIQUE INDEX IF NOT EXISTS "IX_user_payroll_expense_id_unique"
    ON "user" (payroll_expense_id) WHERE payroll_expense_id IS NOT NULL;
