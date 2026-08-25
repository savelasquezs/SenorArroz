BEGIN;

ALTER TABLE product_category ADD COLUMN IF NOT EXISTS storefront_role character varying(20) NOT NULL DEFAULT 'hidden';
ALTER TABLE product ADD COLUMN IF NOT EXISTS storefront_variant_label character varying(80);
ALTER TABLE product ADD COLUMN IF NOT EXISTS storefront_sort_order integer NOT NULL DEFAULT 0;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_product_category_storefront_role') THEN
        ALTER TABLE product_category ADD CONSTRAINT ck_product_category_storefront_role
            CHECK (storefront_role IN ('rice', 'combo', 'beverage', 'addition', 'hidden'));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_product_storefront_sort_order') THEN
        ALTER TABLE product ADD CONSTRAINT ck_product_storefront_sort_order CHECK (storefront_sort_order >= 0);
    END IF;
END $$;

UPDATE product_category
SET storefront_role = CASE name
    WHEN 'Carbonara' THEN 'rice'
    WHEN 'Paisa' THEN 'rice'
    WHEN 'Paisa Chich' THEN 'rice'
    WHEN 'Ranchero' THEN 'rice'
    WHEN 'Ropa Vieja' THEN 'rice'
    WHEN 'Ropa Vieja Chich' THEN 'rice'
    WHEN 'Vegetariano' THEN 'rice'
    WHEN 'Combos' THEN 'combo'
    WHEN 'Gaseosas' THEN 'beverage'
    WHEN 'Adiciones' THEN 'addition'
    ELSE 'hidden'
END;

CREATE TEMP TABLE storefront_product_map (
    category_name text NOT NULL,
    product_name text NOT NULL,
    variant_label text NOT NULL,
    sort_order integer NOT NULL,
    profile_name text
) ON COMMIT DROP;

INSERT INTO storefront_product_map VALUES
('Carbonara','Arroz carbonara Personal','Personal',10,NULL),
('Carbonara','Arroz carbonara Dúo','Dúo',20,NULL),
('Carbonara','Arroz carbonara Trío','Trío',30,NULL),
('Carbonara','Arroz carbonara Familiar','Familiar',40,NULL),
('Carbonara','Arroz carbonara Súper','Súper',50,NULL),
('Paisa','Arroz paisa Personal','Personal',10,NULL),
('Paisa','Arroz paisa Dúo','Dúo',20,NULL),
('Paisa','Arroz paisa Trío','Trío',30,NULL),
('Paisa','Arroz paisa Familiar','Familiar',40,NULL),
('Paisa','Arroz paisa Súper','Súper',50,NULL),
('Paisa Chich','Arroz paisa con chicharrón Personal','Personal',10,NULL),
('Paisa Chich','Arroz paisa con chicharrón Dúo','Dúo',20,NULL),
('Paisa Chich','Arroz paisa con chicharrón Trío','Trío',30,NULL),
('Paisa Chich','Arroz paisa con chicharrón Familiar','Familiar',40,NULL),
('Paisa Chich','Arroz paisa con chicharrón Súper','Súper',50,NULL),
('Ranchero','Arroz ranchero Personal','Personal',10,NULL),
('Ranchero','Arroz ranchero Dúo','Dúo',20,NULL),
('Ranchero','Arroz ranchero Trío','Trío',30,NULL),
('Ranchero','Arroz ranchero Familiar','Familiar',40,NULL),
('Ranchero','Arroz ranchero Súper','Súper',50,NULL),
('Ropa Vieja','Arroz ropa vieja Personal','Personal',10,NULL),
('Ropa Vieja','Arroz ropa vieja Dúo','Dúo',20,NULL),
('Ropa Vieja','Arroz ropa vieja Trío','Trío',30,NULL),
('Ropa Vieja','Arroz ropa vieja Familiar','Familiar',40,NULL),
('Ropa Vieja','Arroz ropa vieja Súper','Súper',50,NULL),
('Ropa Vieja Chich','Arroz ropa vieja con chicharrón Personal','Personal',10,NULL),
('Ropa Vieja Chich','Arroz ropa vieja con chicharrón Dúo','Dúo',20,NULL),
('Ropa Vieja Chich','Arroz ropa vieja con chicharrón Trío','Trío',30,NULL),
('Ropa Vieja Chich','Arroz ropa vieja con chicharrón Familiar','Familiar',40,NULL),
('Ropa Vieja Chich','Arroz ropa vieja con chicharrón Súper','Súper',50,NULL),
('Vegetariano','Arroz vegetariano Personal','Personal',10,'Arroz vegetariano'),
('Vegetariano','Arroz vegetariano Dúo','Dúo',20,'Arroz vegetariano'),
('Vegetariano','Arroz vegetariano Trío','Trío',30,'Arroz vegetariano'),
('Vegetariano','Arroz vegetariano Familiar','Familiar',40,'Arroz vegetariano'),
('Vegetariano','Arroz vegetariano Súper','Súper',50,'Arroz vegetariano'),
('Combos','Combochicharrón','1–2 personas',10,'Combochicharrón'),
('Combos','Costicombo','1–2 personas',20,'Costicombo'),
('Adiciones','Adición de Maduro','Porción',10,'Maduro'),
('Adiciones','Agridulce','Porción',10,'Agridulce'),
('Adiciones','Carne desmechada 100 g','100 g',10,'Carne desmechada'),
('Adiciones','Carne desmechada 200 g','200 g',20,'Carne desmechada'),
('Adiciones','Chicharrón 250 gr','250 g',10,'Chicharrón'),
('Adiciones','Costilla BBQ 500 gr','500 g',10,'Costilla BBQ'),
('Adiciones','Costilla picada 200g','200 g',10,'Costilla picada'),
('Adiciones','Papas a la francesa 250 gr','250 g',10,'Papas a la francesa'),
('Adiciones','Papas a la francesa 500 gr','500 g',20,'Papas a la francesa'),
('Adiciones','Paquete de pan','Paquete',10,'Pan'),
('Adiciones','Paquete de panes','Paquete',20,'Pan'),
('Adiciones','Tocineta 200g','200 g',10,'Tocineta'),
('Adiciones','Trocitos de chicharrón 200g','200 g',10,'Trocitos de chicharrón'),
('Adiciones','Yuca x5 unidades','5 unidades',10,'Yuca'),
('Adiciones','Yuca x12 unidades','12 unidades',20,'Yuca'),
('Gaseosas','Agua 600ml','600 ml',10,'Agua'),
('Gaseosas','Agua saborizada','Personal',10,'Agua saborizada'),
('Gaseosas','CocaCola PET 400','400 ml',10,'Coca-Cola'),
('Gaseosas','CocaCola 1.5L','1.5 L',20,'Coca-Cola'),
('Gaseosas','CocaCola 3L','3 L',30,'Coca-Cola'),
('Gaseosas','Colombiana pet 400','400 ml',10,'Colombiana'),
('Gaseosas','Colombiana 1 litro','1 L',20,'Colombiana'),
('Gaseosas','Colombiana 1.5L','1.5 L',30,'Colombiana'),
('Gaseosas','Colombiana 3L','3 L',40,'Colombiana'),
('Gaseosas','H2O 1.5L','1.5 L',10,'H2O'),
('Gaseosas','Hit frutos 1Litro','1 L',10,'Hit frutos'),
('Gaseosas','Hit Mango 1Litro','1 L',10,'Hit mango'),
('Gaseosas','Manzana Personal','Personal',10,'Manzana'),
('Gaseosas','Manzana litro y medio','1.5 L',20,'Manzana'),
('Gaseosas','Manzana 3L','3 L',30,'Manzana'),
('Gaseosas','Naranjada Personal','Personal',10,'Naranjada'),
('Gaseosas','Naranjada 1 litro econo','1 L',20,'Naranjada'),
('Gaseosas','Naranjada 1.5L','1.5 L',30,'Naranjada'),
('Gaseosas','Naranjada Mega','Mega',40,'Naranjada'),
('Gaseosas','Pepsi pet 400','400 ml',10,'Pepsi'),
('Gaseosas','Pepsi Econo','1 L',20,'Pepsi'),
('Gaseosas','Pepsi econo zero','1 L sin azúcar',30,'Pepsi'),
('Gaseosas','Pepsi 1.5L','1.5 L',40,'Pepsi'),
('Gaseosas','Premio PET 400','400 ml',10,'Premio'),
('Gaseosas','Premio 1.5L','1.5 L',20,'Premio'),
('Gaseosas','Quatro PET 400','400 ml',10,'Quatro'),
('Gaseosas','Quatro 1.5L','1.5 L',20,'Quatro'),
('Gaseosas','Quatro 3L','3 L',30,'Quatro'),
('Gaseosas','Seven up 1.5 L','1.5 L',10,'Seven Up'),
('Gaseosas','Sprite PET 400','400 ml',10,'Sprite'),
('Gaseosas','Sprite 1.5L','1.5 L',20,'Sprite'),
('Gaseosas','Uva 1 litro','1 L',10,'Uva'),
('Gaseosas','Uva 1.5L','1.5 L',20,'Uva');

UPDATE product p
SET storefront_variant_label = m.variant_label,
    storefront_sort_order = m.sort_order
FROM product_category c, storefront_product_map m
WHERE p.category_id = c.id
  AND c.name = m.category_name
  AND p.name = m.product_name;

INSERT INTO commercial_profile (branch_id, name)
SELECT DISTINCT c.branch_id, m.profile_name
FROM storefront_product_map m
JOIN product_category c ON c.name = m.category_name
WHERE m.profile_name IS NOT NULL
ON CONFLICT (branch_id, name) DO NOTHING;

UPDATE product p
SET commercial_profile_id = cp.id
FROM product_category c
JOIN storefront_product_map m ON m.category_name = c.name
JOIN commercial_profile cp ON cp.branch_id = c.branch_id AND cp.name = m.profile_name
WHERE p.category_id = c.id
  AND p.name = m.product_name
  AND m.profile_name IS NOT NULL;

UPDATE product p
SET serves_people_min = 1, serves_people_max = 2
FROM product_category c
WHERE p.category_id = c.id
  AND c.name = 'Combos'
  AND p.name IN ('Combochicharrón', 'Costicombo');

WITH vegetarian_profiles AS (
    SELECT cp.branch_id,
           cp.id,
           CASE WHEN NULLIF(BTRIM(cp.photo_url), '') IS NOT NULL THEN 1 ELSE 0 END AS has_photo,
           CASE WHEN NULLIF(BTRIM(cp.description), '') IS NOT NULL THEN 1 ELSE 0 END AS has_description,
           COUNT(p.id) FILTER (WHERE c.name = 'Vegetariano') AS linked_products
    FROM commercial_profile cp
    LEFT JOIN product p ON p.commercial_profile_id = cp.id
    LEFT JOIN product_category c ON c.id = p.category_id
    WHERE LOWER(BTRIM(cp.name)) = LOWER('Arroz vegetariano')
       OR c.name = 'Vegetariano'
    GROUP BY cp.branch_id, cp.id, cp.photo_url, cp.description
), canonical_vegetarian_profile AS (
    SELECT DISTINCT ON (branch_id) branch_id, id
    FROM vegetarian_profiles
    ORDER BY branch_id, has_photo DESC, has_description DESC, linked_products DESC, id
)
UPDATE product p
SET commercial_profile_id = canonical.id
FROM product_category c
JOIN canonical_vegetarian_profile canonical ON canonical.branch_id = c.branch_id
WHERE p.category_id = c.id
  AND c.name = 'Vegetariano';

WITH canonical_vegetarian_profile AS (
    SELECT c.branch_id, MIN(p.commercial_profile_id) AS id
    FROM product p
    JOIN product_category c ON c.id = p.category_id
    WHERE c.name = 'Vegetariano'
      AND p.commercial_profile_id IS NOT NULL
    GROUP BY c.branch_id
)
DELETE FROM commercial_profile duplicate
USING canonical_vegetarian_profile canonical
WHERE duplicate.branch_id = canonical.branch_id
  AND duplicate.id <> canonical.id
  AND LOWER(BTRIM(duplicate.name)) = LOWER('Arroz vegetariano')
  AND NOT EXISTS (SELECT 1 FROM product p WHERE p.commercial_profile_id = duplicate.id);

CREATE INDEX IF NOT EXISTS ix_product_category_storefront
    ON product (category_id, storefront_sort_order, id) WHERE active = true;

COMMIT;
