-- =============================================================================
-- Peso unitario (weight_grams) por tamaño: productos "Arroz ..." con/sin chicharrón
-- =============================================================================
-- Reglas:
--   Con chicharrón (nombre contiene "chichar"): Personal 600g, Dúo 980g, Trío 1350g,
--   Familiar 1900g, Súper 2600g.
--   Sin chicharrón: Personal 520g, Dúo 920g, Trío 1200g, Familiar 1750g, Súper 2400g.
-- Solo filas cuyo nombre empieza por "Arroz".
--
-- IMPORTANTE: ILIKE '%duo%' NO coincide con "Dúo" en PostgreSQL (no ignora tildes).
-- Por eso se usa la última palabra del nombre (tamaño) con lower(...).
--
-- Uso local:
--   psql -h 127.0.0.1 -p 5433 -U postgres -d senorArroz -f Scripts/update-product-weights-by-size.sql
-- Railway:
--   psql "$DATABASE_URL" -f Scripts/update-product-weights-by-size.sql
-- =============================================================================

BEGIN;

-- Expresión reutilizable: última palabra del nombre (tamaño), en minúsculas.
-- Ej.: "Arroz ropa vieja con chicharrón Dúo" → 'dúo'

-- ---------- Con chicharrón ----------
UPDATE product SET weight_grams = 600
WHERE name ILIKE 'Arroz%'
  AND name ILIKE '%chicharr%'
  AND lower(reverse(split_part(reverse(trim(name)), ' ', 1))) = 'personal';

UPDATE product SET weight_grams = 980
WHERE name ILIKE 'Arroz%'
  AND name ILIKE '%chicharr%'
  AND lower(reverse(split_part(reverse(trim(name)), ' ', 1))) ilike '%d%o';

UPDATE product SET weight_grams = 1350
WHERE name ILIKE 'Arroz%'
  AND name ILIKE '%chicharr%'
  AND lower(reverse(split_part(reverse(trim(name)), ' ', 1))) ilike '%tr%o';

UPDATE product SET weight_grams = 1900
WHERE name ILIKE 'Arroz%'
  AND name ILIKE '%chicharr%'
  AND lower(reverse(split_part(reverse(trim(name)), ' ', 1))) = 'familiar';

UPDATE product SET weight_grams = 2600
WHERE name ILIKE 'Arroz%'
  AND name ILIKE '%chicharr%'
  AND lower(reverse(split_part(reverse(trim(name)), ' ', 1))) ilike '%s%per';

-- ---------- Sin chicharrón ----------
UPDATE product SET weight_grams = 520
WHERE name ILIKE 'Arroz%'
  AND name NOT ILIKE '%chicharr%'
  AND lower(reverse(split_part(reverse(trim(name)), ' ', 1))) = 'personal';

UPDATE product SET weight_grams = 920
WHERE name ILIKE 'Arroz%'
  AND name NOT ILIKE '%chicharr%'
  AND lower(reverse(split_part(reverse(trim(name)), ' ', 1))) ilike '%d%o';

UPDATE product SET weight_grams = 1200
WHERE name ILIKE 'Arroz%'
  AND name NOT ILIKE '%chicharr%'
  AND lower(reverse(split_part(reverse(trim(name)), ' ', 1))) ilike '%tr%o';

UPDATE product SET weight_grams = 1750
WHERE name ILIKE 'Arroz%'
  AND name NOT ILIKE '%chicharr%'
  AND lower(reverse(split_part(reverse(trim(name)), ' ', 1))) = 'familiar';

UPDATE product SET weight_grams = 2400
WHERE name ILIKE 'Arroz%'
  AND name NOT ILIKE '%chicharr%'
  AND lower(reverse(split_part(reverse(trim(name)), ' ', 1))) ilike '%s%per';

COMMIT;

-- Verificación:
-- SELECT id, name, weight_grams FROM product WHERE name ILIKE 'Arroz%' ORDER BY id;
