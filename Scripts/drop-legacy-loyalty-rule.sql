-- Elimina la tabla loyalty_rule y la columna order.loyalty_rule_id (reemplazadas por loyalty_cycle_step).
-- Ejecutar en producción tras backup (orden seguro: FK → índice → columna → tabla).

ALTER TABLE "order" DROP CONSTRAINT IF EXISTS "FK_order_loyalty_rule_loyalty_rule_id";
DROP INDEX IF EXISTS "IX_order_loyalty_rule_id";
ALTER TABLE "order" DROP COLUMN IF EXISTS loyalty_rule_id;
DROP INDEX IF EXISTS "IX_loyalty_rule_branch_id";
DROP TABLE IF EXISTS loyalty_rule;
