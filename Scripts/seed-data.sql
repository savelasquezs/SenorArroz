-- ==============================================================================
-- Script: Datos Iniciales para SenorArroz API
-- Propósito: Crear datos básicos necesarios para que la aplicación funcione
-- Fecha: Noviembre 2024
-- ==============================================================================

-- Este script es IDEMPOTENTE: usa ON CONFLICT DO NOTHING para evitar errores
-- Puede ejecutarse múltiples veces sin problemas

BEGIN;

-- ==============================================================================
-- 1. BRANCH (Sucursal)
-- ==============================================================================

INSERT INTO branch (name, address, phone1, phone2) 
VALUES ('Santander', 'calle 108a # 77d-30', '0000000000', NULL)
ON CONFLICT DO NOTHING;

-- Obtener el ID de la sucursal creada (asumimos ID = 1 si es la primera)
-- Si ya existe, usar el ID existente
DO $$
DECLARE
    branch_id_var INTEGER;
BEGIN
    SELECT id INTO branch_id_var FROM branch WHERE name = 'Santander' LIMIT 1;
    
    IF branch_id_var IS NULL THEN
        -- Si no existe, usar el siguiente ID disponible
        SELECT COALESCE(MAX(id), 0) + 1 INTO branch_id_var FROM branch;
    END IF;

    -- ==============================================================================
    -- 2. USUARIOS
    -- ==============================================================================

    -- Usuario 1: Superadmin (Santiago)
    INSERT INTO "user" (branch_id, role, name, email, phone, password_hash, active)
    VALUES (
        branch_id_var,
        'superadmin',
        'Santiago',
        'santyvano@outlook.com',
        '0000000000',
        '$2a$11$5ERmxaaUoW.MLkirB/l22eyaZxF1OCK2kjtN9DtYo14k.o2XmkyBy', -- Santiago1234
        true
    )
    ON CONFLICT DO NOTHING;

    -- Usuario 2: Admin (Luis)
    -- NOTA: El hash BCrypt para "Luis1234" debe generarse usando BCrypt.Net con workFactor 12
    -- Para generar el hash, ejecutar: 
    -- BCrypt.Net.BCrypt.HashPassword("Luis1234", workFactor: 12)
    -- O usar un generador online de BCrypt
    INSERT INTO "user" (branch_id, role, name, email, phone, password_hash, active)
    VALUES (
        branch_id_var,
        'admin',
        'Luis',
        'lsvillorina@gmail.com',
        '0000000000',
        '$2a$12$PLACEHOLDER_FOR_LUIS1234_HASH_REPLACE_THIS', -- TODO: Reemplazar con hash real de Luis1234
        true
    )
    ON CONFLICT DO NOTHING;

    -- ==============================================================================
    -- 3. PRODUCT CATEGORIES (Categorías de Producto)
    -- ==============================================================================

    INSERT INTO product_category (branch_id, name)
    VALUES 
        (branch_id_var, 'Bebidas'),
        (branch_id_var, 'Entradas'),
        (branch_id_var, 'Platos Principales'),
        (branch_id_var, 'Postres'),
        (branch_id_var, 'Acompañamientos')
    ON CONFLICT DO NOTHING;

    -- ==============================================================================
    -- 4. PRODUCTS (Productos de Prueba - Restaurante)
    -- ==============================================================================

    -- Bebidas (asumiendo que la categoría Bebidas tiene ID = 1 o el primero disponible)
    INSERT INTO product (category_id, name, price, stock, active)
    SELECT 
        (SELECT id FROM product_category WHERE branch_id = branch_id_var AND name = 'Bebidas' LIMIT 1),
        name,
        price,
        stock,
        active
    FROM (VALUES
        ('Coca Cola', 3000, 100, true),
        ('Agua', 2000, 100, true),
        ('Jugo Natural', 4000, 50, true),
        ('Limonada', 3500, 50, true),
        ('Gaseosa Personal', 2500, 100, true)
    ) AS v(name, price, stock, active)
    WHERE NOT EXISTS (
        SELECT 1 FROM product p
        INNER JOIN product_category pc ON p.category_id = pc.id
        WHERE pc.branch_id = branch_id_var 
        AND pc.name = 'Bebidas'
        AND p.name = v.name
    );

    -- Entradas
    INSERT INTO product (category_id, name, price, stock, active)
    SELECT 
        (SELECT id FROM product_category WHERE branch_id = branch_id_var AND name = 'Entradas' LIMIT 1),
        name,
        price,
        stock,
        active
    FROM (VALUES
        ('Patacones con Hogao', 8000, 30, true),
        ('Arepas con Queso', 6000, 40, true),
        ('Empanadas (3 unidades)', 7000, 50, true),
        ('Chicharrón', 10000, 25, true),
        ('Ensalada de la Casa', 9000, 20, true)
    ) AS v(name, price, stock, active)
    WHERE NOT EXISTS (
        SELECT 1 FROM product p
        INNER JOIN product_category pc ON p.category_id = pc.id
        WHERE pc.branch_id = branch_id_var 
        AND pc.name = 'Entradas'
        AND p.name = v.name
    );

    -- Platos Principales
    INSERT INTO product (category_id, name, price, stock, active)
    SELECT 
        (SELECT id FROM product_category WHERE branch_id = branch_id_var AND name = 'Platos Principales' LIMIT 1),
        name,
        price,
        stock,
        active
    FROM (VALUES
        ('Arroz con Pollo', 15000, 20, true),
        ('Bandeja Paisa', 18000, 15, true),
        ('Sancocho', 12000, 25, true),
        ('Ajiaco', 14000, 20, true),
        ('Pechuga a la Plancha', 16000, 18, true),
        ('Pescado Frito', 17000, 12, true),
        ('Carne Asada', 19000, 15, true),
        ('Chuleta de Cerdo', 17500, 10, true)
    ) AS v(name, price, stock, active)
    WHERE NOT EXISTS (
        SELECT 1 FROM product p
        INNER JOIN product_category pc ON p.category_id = pc.id
        WHERE pc.branch_id = branch_id_var 
        AND pc.name = 'Platos Principales'
        AND p.name = v.name
    );

    -- Postres
    INSERT INTO product (category_id, name, price, stock, active)
    SELECT 
        (SELECT id FROM product_category WHERE branch_id = branch_id_var AND name = 'Postres' LIMIT 1),
        name,
        price,
        stock,
        active
    FROM (VALUES
        ('Flan de Caramelo', 6000, 15, true),
        ('Tres Leches', 7000, 12, true),
        ('Helado', 5000, 20, true),
        ('Brownie con Helado', 8000, 10, true),
        ('Mazamorra', 4000, 25, true)
    ) AS v(name, price, stock, active)
    WHERE NOT EXISTS (
        SELECT 1 FROM product p
        INNER JOIN product_category pc ON p.category_id = pc.id
        WHERE pc.branch_id = branch_id_var 
        AND pc.name = 'Postres'
        AND p.name = v.name
    );

    -- Acompañamientos
    INSERT INTO product (category_id, name, price, stock, active)
    SELECT 
        (SELECT id FROM product_category WHERE branch_id = branch_id_var AND name = 'Acompañamientos' LIMIT 1),
        name,
        price,
        stock,
        active
    FROM (VALUES
        ('Arroz', 2000, 100, true),
        ('Frijoles', 3000, 80, true),
        ('Papas Fritas', 4000, 60, true),
        ('Ensalada', 3000, 50, true),
        ('Plátano Maduro', 2500, 70, true)
    ) AS v(name, price, stock, active)
    WHERE NOT EXISTS (
        SELECT 1 FROM product p
        INNER JOIN product_category pc ON p.category_id = pc.id
        WHERE pc.branch_id = branch_id_var 
        AND pc.name = 'Acompañamientos'
        AND p.name = v.name
    );

    RAISE NOTICE 'Datos iniciales creados para branch_id: %', branch_id_var;
END $$;

COMMIT;

-- ==============================================================================
-- NOTA IMPORTANTE:
-- ==============================================================================
-- El hash BCrypt para el usuario Luis (Luis1234) está como placeholder.
-- Debes reemplazar '$2a$12$PLACEHOLDER_FOR_LUIS1234_HASH_REPLACE_THIS' 
-- con el hash real generado usando:
--
-- Opción 1: Usar BCrypt.Net en C#
--   BCrypt.Net.BCrypt.HashPassword("Luis1234", workFactor: 12)
--
-- Opción 2: Usar un generador online de BCrypt
--   https://bcrypt-generator.com/ (usar rounds: 12)
--
-- Opción 3: Ejecutar este comando en PowerShell después de compilar:
--   $assembly = [System.Reflection.Assembly]::LoadFrom("SenorArroz.API\bin\Debug\net9.0\BCrypt.Net-Next.dll")
--   $hash = [BCrypt.Net.BCrypt]::HashPassword("Luis1234", 12)
--   Write-Host $hash
-- ==============================================================================

