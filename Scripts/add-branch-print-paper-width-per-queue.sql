-- Ancho de papel térmico por cola: cocina, domicilio, caja (58 / 80 mm).
-- Idempotente. Ejecutar en producción tras desplegar API + agente nuevos.

ALTER TABLE branch_print_settings ADD COLUMN IF NOT EXISTS paper_width_mm_kitchen smallint;
ALTER TABLE branch_print_settings ADD COLUMN IF NOT EXISTS paper_width_mm_delivery smallint;
ALTER TABLE branch_print_settings ADD COLUMN IF NOT EXISTS paper_width_mm_cashier smallint;

UPDATE branch_print_settings SET
    paper_width_mm_kitchen = COALESCE(paper_width_mm_kitchen, paper_width_mm),
    paper_width_mm_delivery = COALESCE(paper_width_mm_delivery, paper_width_mm),
    paper_width_mm_cashier = COALESCE(paper_width_mm_cashier, paper_width_mm)
WHERE paper_width_mm_kitchen IS NULL
   OR paper_width_mm_delivery IS NULL
   OR paper_width_mm_cashier IS NULL;

ALTER TABLE branch_print_settings ALTER COLUMN paper_width_mm_kitchen SET NOT NULL;
ALTER TABLE branch_print_settings ALTER COLUMN paper_width_mm_kitchen SET DEFAULT 58;
ALTER TABLE branch_print_settings ALTER COLUMN paper_width_mm_delivery SET NOT NULL;
ALTER TABLE branch_print_settings ALTER COLUMN paper_width_mm_delivery SET DEFAULT 58;
ALTER TABLE branch_print_settings ALTER COLUMN paper_width_mm_cashier SET NOT NULL;
ALTER TABLE branch_print_settings ALTER COLUMN paper_width_mm_cashier SET DEFAULT 58;

UPDATE branch_print_settings SET paper_width_mm = paper_width_mm_kitchen WHERE paper_width_mm IS DISTINCT FROM paper_width_mm_kitchen;
