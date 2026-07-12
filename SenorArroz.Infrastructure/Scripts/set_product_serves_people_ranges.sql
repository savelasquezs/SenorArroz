BEGIN;

-- Asigna el rango de personas a las presentaciones de arroz.
-- Los pesos se usan como respaldo porque algunos nombres históricos quedaron
-- guardados o se muestran con problemas de codificación (Dúo, Trío y Súper).
WITH product_ranges AS (
    SELECT
        id,
        CASE
            WHEN lower(name) ~ '(superfamiliar|super familiar|súper|super)[[:space:]]*$'
                 OR weight_grams IN (2400, 2600) THEN 7
            WHEN lower(name) ~ 'familiar[[:space:]]*$'
                 OR weight_grams IN (1750, 1900) THEN 5
            WHEN lower(name) ~ '(trío|trio)[[:space:]]*$'
                 OR weight_grams IN (1200, 1350) THEN 3
            WHEN lower(name) ~ '(dúo|duo)[[:space:]]*$'
                 OR weight_grams IN (920, 980) THEN 2
            WHEN lower(name) ~ 'personal[[:space:]]*$'
                 OR weight_grams IN (520, 600) THEN 1
        END AS serves_min,
        CASE
            WHEN lower(name) ~ '(superfamiliar|super familiar|súper|super)[[:space:]]*$'
                 OR weight_grams IN (2400, 2600) THEN 9
            WHEN lower(name) ~ 'familiar[[:space:]]*$'
                 OR weight_grams IN (1750, 1900) THEN 6
            WHEN lower(name) ~ '(trío|trio)[[:space:]]*$'
                 OR weight_grams IN (1200, 1350) THEN 4
            WHEN lower(name) ~ '(dúo|duo)[[:space:]]*$'
                 OR weight_grams IN (920, 980) THEN 3
            WHEN lower(name) ~ 'personal[[:space:]]*$'
                 OR weight_grams IN (520, 600) THEN 2
        END AS serves_max
    FROM product
    WHERE lower(name) LIKE 'arroz%'
)
UPDATE product AS p
SET
    serves_people_min = r.serves_min,
    serves_people_max = r.serves_max,
    updated_at = NOW()
FROM product_ranges AS r
WHERE p.id = r.id
  AND r.serves_min IS NOT NULL
  AND (
      p.serves_people_min IS DISTINCT FROM r.serves_min
      OR p.serves_people_max IS DISTINCT FROM r.serves_max
  );

-- Permite verificar qué arroces no coincidieron con una presentación conocida.
DO $$
DECLARE
    unmatched_count integer;
BEGIN
    SELECT COUNT(*)
    INTO unmatched_count
    FROM product
    WHERE lower(name) LIKE 'arroz%'
      AND (serves_people_min IS NULL OR serves_people_max IS NULL);

    RAISE NOTICE 'Productos de arroz sin rango de personas: %', unmatched_count;
END $$;

COMMIT;

-- Verificación opcional después de ejecutar el archivo:
-- SELECT id, name, weight_grams, serves_people_min, serves_people_max
-- FROM product
-- WHERE lower(name) LIKE 'arroz%'
-- ORDER BY name;
