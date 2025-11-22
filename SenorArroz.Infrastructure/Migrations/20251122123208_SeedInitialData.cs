using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SenorArroz.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeedInitialData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ==============================================================================
            // DATOS INICIALES - Idempotente usando DO $$ blocks
            // ==============================================================================

            // 1. SUCURSAL
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    branch_id_var INTEGER;
                BEGIN
                    -- Insertar sucursal si no existe
                    INSERT INTO branch (name, address, phone1, phone2) 
                    VALUES ('Santander', 'calle 108a # 77d-30', '0000000000', NULL)
                    ON CONFLICT DO NOTHING;
                    
                    -- Obtener ID de la sucursal
                    SELECT id INTO branch_id_var FROM branch WHERE name = 'Santander' LIMIT 1;
                    
                    -- 2. USUARIOS
                    INSERT INTO ""user"" (branch_id, role, name, email, phone, password_hash, active)
                    VALUES 
                        (branch_id_var, 'superadmin', 'Santiago', 'santyvano@outlook.com', '0000000000', '$2a$11$5ERmxaaUoW.MLkirB/l22eyaZxF1OCK2kjtN9DtYo14k.o2XmkyBy', true),
                        (branch_id_var, 'admin', 'Daniel Alvarez', 'daniel@daniel.com', '3125486596', '$2a$12$FR0XMK.2RulbbSgH5un0Uu8fZ/KcfXMZdGtng4N8jIiaxe/nbkanS', true),
                        (branch_id_var, 'deliveryman', 'Abelardo', 'abelardo@abelardo.com', '3226474857', '$2a$12$QRFDuTrE7HfTefR5syYkV.6JQ62bGYheR56Qhs9TCuhAVzr0FnGGy', true),
                        (branch_id_var, 'deliveryman', 'Maikol Martinez Serna', 'maikol@maikol.com', '3021563245', '$2a$12$9wl/bOW4736yr3ssYHweUekgRch7RnafYTKHo2RVKcUH1kYJlSVbG', true),
                        (branch_id_var, 'kitchen', 'juan', 'juan@juan.com', '3256325695', '$2a$12$oPImGwKsmXYVhlY1spD3ROyNZXDGchLNnIYl9B97KY1Sok9Cb6Wna', true)
                    ON CONFLICT DO NOTHING;
                    
                    -- 3. BARRIOS DEL NORTE DE MEDELLÍN
                    INSERT INTO neighborhood (branch_id, name, delivery_fee)
                    VALUES 
                        (branch_id_var, 'Castilla', 3000),
                        (branch_id_var, 'Santander', 4000),
                        (branch_id_var, 'Pedregal', 2500),
                        (branch_id_var, 'Florencia', 3500),
                        (branch_id_var, 'Picacho', 5000)
                    ON CONFLICT DO NOTHING;
                    
                    -- 4. BANCO
                    INSERT INTO bank (branch_id, name, image_url, active)
                    VALUES (branch_id_var, 'Bancolombia', NULL, true)
                    ON CONFLICT DO NOTHING;
                END $$;
            ");

            // 5. APP (Didi) - Debe ejecutarse después del banco
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    branch_id_var INTEGER;
                    bank_id_var INTEGER;
                BEGIN
                    SELECT id INTO branch_id_var FROM branch WHERE name = 'Santander' LIMIT 1;
                    SELECT id INTO bank_id_var FROM bank WHERE name = 'Bancolombia' AND branch_id = branch_id_var LIMIT 1;
                    
                    IF bank_id_var IS NOT NULL THEN
                        INSERT INTO app (bank_id, name, image_url, active)
                        VALUES (bank_id_var, 'Didi', NULL, true)
                        ON CONFLICT DO NOTHING;
                    END IF;
                END $$;
            ");

            // 6. CATEGORÍAS DE PRODUCTOS
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    branch_id_var INTEGER;
                BEGIN
                    SELECT id INTO branch_id_var FROM branch WHERE name = 'Santander' LIMIT 1;
                    
                    INSERT INTO product_category (branch_id, name)
                    VALUES 
                        (branch_id_var, 'Bebidas'),
                        (branch_id_var, 'Entradas'),
                        (branch_id_var, 'Platos Principales'),
                        (branch_id_var, 'Postres'),
                        (branch_id_var, 'Acompañamientos')
                    ON CONFLICT DO NOTHING;
                END $$;
            ");

            // 7. PRODUCTOS
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    branch_id_var INTEGER;
                    bebidas_cat_id INTEGER;
                    entradas_cat_id INTEGER;
                    platos_cat_id INTEGER;
                    postres_cat_id INTEGER;
                    acompanamientos_cat_id INTEGER;
                BEGIN
                    SELECT id INTO branch_id_var FROM branch WHERE name = 'Santander' LIMIT 1;
                    SELECT id INTO bebidas_cat_id FROM product_category WHERE branch_id = branch_id_var AND name = 'Bebidas' LIMIT 1;
                    SELECT id INTO entradas_cat_id FROM product_category WHERE branch_id = branch_id_var AND name = 'Entradas' LIMIT 1;
                    SELECT id INTO platos_cat_id FROM product_category WHERE branch_id = branch_id_var AND name = 'Platos Principales' LIMIT 1;
                    SELECT id INTO postres_cat_id FROM product_category WHERE branch_id = branch_id_var AND name = 'Postres' LIMIT 1;
                    SELECT id INTO acompanamientos_cat_id FROM product_category WHERE branch_id = branch_id_var AND name = 'Acompañamientos' LIMIT 1;
                    
                    -- Bebidas
                    INSERT INTO product (category_id, name, price, stock, active)
                    SELECT bebidas_cat_id, name, price, stock, active
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
                    SELECT entradas_cat_id, name, price, stock, active
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
                    SELECT platos_cat_id, name, price, stock, active
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
                    SELECT postres_cat_id, name, price, stock, active
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
                    SELECT acompanamientos_cat_id, name, price, stock, active
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
                END $$;
            ");

            // 8. CLIENTES CON DIRECCIONES Y COORDENADAS
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    branch_id_var INTEGER;
                    castilla_id INTEGER;
                    santander_id INTEGER;
                    pedregal_id INTEGER;
                    florencia_id INTEGER;
                    picacho_id INTEGER;
                    customer1_id INTEGER;
                    customer2_id INTEGER;
                    customer3_id INTEGER;
                    customer4_id INTEGER;
                    customer5_id INTEGER;
                    customer6_id INTEGER;
                    customer7_id INTEGER;
                    customer8_id INTEGER;
                BEGIN
                    SELECT id INTO branch_id_var FROM branch WHERE name = 'Santander' LIMIT 1;
                    SELECT id INTO castilla_id FROM neighborhood WHERE branch_id = branch_id_var AND name = 'Castilla' LIMIT 1;
                    SELECT id INTO santander_id FROM neighborhood WHERE branch_id = branch_id_var AND name = 'Santander' LIMIT 1;
                    SELECT id INTO pedregal_id FROM neighborhood WHERE branch_id = branch_id_var AND name = 'Pedregal' LIMIT 1;
                    SELECT id INTO florencia_id FROM neighborhood WHERE branch_id = branch_id_var AND name = 'Florencia' LIMIT 1;
                    SELECT id INTO picacho_id FROM neighborhood WHERE branch_id = branch_id_var AND name = 'Picacho' LIMIT 1;
                    
                    -- Cliente 1: María González - Castilla
                    INSERT INTO customer (branch_id, name, phone1, phone2, active)
                    VALUES (branch_id_var, 'María González', '3001234567', NULL, true)
                    ON CONFLICT DO NOTHING
                    RETURNING id INTO customer1_id;
                    
                    IF customer1_id IS NULL THEN
                        SELECT id INTO customer1_id FROM customer WHERE branch_id = branch_id_var AND phone1 = '3001234567' LIMIT 1;
                    END IF;
                    
                    IF customer1_id IS NOT NULL AND castilla_id IS NOT NULL THEN
                        INSERT INTO address (customer_id, neighborhood_id, address, additional_info, delivery_fee, latitude, longitude, is_primary)
                        VALUES (customer1_id, castilla_id, 'Calle 65 #45-20, Castilla', NULL, 3000, 6.2810, -75.5710, true)
                        ON CONFLICT DO NOTHING;
                    END IF;
                    
                    -- Cliente 2: Carlos Ramírez - Santander
                    INSERT INTO customer (branch_id, name, phone1, phone2, active)
                    VALUES (branch_id_var, 'Carlos Ramírez', '3102345678', NULL, true)
                    ON CONFLICT DO NOTHING
                    RETURNING id INTO customer2_id;
                    
                    IF customer2_id IS NULL THEN
                        SELECT id INTO customer2_id FROM customer WHERE branch_id = branch_id_var AND phone1 = '3102345678' LIMIT 1;
                    END IF;
                    
                    IF customer2_id IS NOT NULL AND santander_id IS NOT NULL THEN
                        INSERT INTO address (customer_id, neighborhood_id, address, additional_info, delivery_fee, latitude, longitude, is_primary)
                        VALUES (customer2_id, santander_id, 'Carrera 50 #30-15, Santander', NULL, 4000, 6.2910, -75.5610, true)
                        ON CONFLICT DO NOTHING;
                    END IF;
                    
                    -- Cliente 3: Ana Martínez - Pedregal
                    INSERT INTO customer (branch_id, name, phone1, phone2, active)
                    VALUES (branch_id_var, 'Ana Martínez', '3203456789', NULL, true)
                    ON CONFLICT DO NOTHING
                    RETURNING id INTO customer3_id;
                    
                    IF customer3_id IS NULL THEN
                        SELECT id INTO customer3_id FROM customer WHERE branch_id = branch_id_var AND phone1 = '3203456789' LIMIT 1;
                    END IF;
                    
                    IF customer3_id IS NOT NULL AND pedregal_id IS NOT NULL THEN
                        INSERT INTO address (customer_id, neighborhood_id, address, additional_info, delivery_fee, latitude, longitude, is_primary)
                        VALUES (customer3_id, pedregal_id, 'Calle 70 #55-30, Pedregal', NULL, 2500, 6.3010, -75.5510, true)
                        ON CONFLICT DO NOTHING;
                    END IF;
                    
                    -- Cliente 4: Luis Pérez - Florencia
                    INSERT INTO customer (branch_id, name, phone1, phone2, active)
                    VALUES (branch_id_var, 'Luis Pérez', '3154567890', NULL, true)
                    ON CONFLICT DO NOTHING
                    RETURNING id INTO customer4_id;
                    
                    IF customer4_id IS NULL THEN
                        SELECT id INTO customer4_id FROM customer WHERE branch_id = branch_id_var AND phone1 = '3154567890' LIMIT 1;
                    END IF;
                    
                    IF customer4_id IS NOT NULL AND florencia_id IS NOT NULL THEN
                        INSERT INTO address (customer_id, neighborhood_id, address, additional_info, delivery_fee, latitude, longitude, is_primary)
                        VALUES (customer4_id, florencia_id, 'Carrera 45 #25-10, Florencia', NULL, 3500, 6.2710, -75.5810, true)
                        ON CONFLICT DO NOTHING;
                    END IF;
                    
                    -- Cliente 5: Sofía Rodríguez - Picacho
                    INSERT INTO customer (branch_id, name, phone1, phone2, active)
                    VALUES (branch_id_var, 'Sofía Rodríguez', '3005678901', NULL, true)
                    ON CONFLICT DO NOTHING
                    RETURNING id INTO customer5_id;
                    
                    IF customer5_id IS NULL THEN
                        SELECT id INTO customer5_id FROM customer WHERE branch_id = branch_id_var AND phone1 = '3005678901' LIMIT 1;
                    END IF;
                    
                    IF customer5_id IS NOT NULL AND picacho_id IS NOT NULL THEN
                        INSERT INTO address (customer_id, neighborhood_id, address, additional_info, delivery_fee, latitude, longitude, is_primary)
                        VALUES (customer5_id, picacho_id, 'Calle 80 #60-40, Picacho', NULL, 5000, 6.3110, -75.5410, true)
                        ON CONFLICT DO NOTHING;
                    END IF;
                    
                    -- Cliente 6: Diego Torres - Castilla
                    INSERT INTO customer (branch_id, name, phone1, phone2, active)
                    VALUES (branch_id_var, 'Diego Torres', '3106789012', NULL, true)
                    ON CONFLICT DO NOTHING
                    RETURNING id INTO customer6_id;
                    
                    IF customer6_id IS NULL THEN
                        SELECT id INTO customer6_id FROM customer WHERE branch_id = branch_id_var AND phone1 = '3106789012' LIMIT 1;
                    END IF;
                    
                    IF customer6_id IS NOT NULL AND castilla_id IS NOT NULL THEN
                        INSERT INTO address (customer_id, neighborhood_id, address, additional_info, delivery_fee, latitude, longitude, is_primary)
                        VALUES (customer6_id, castilla_id, 'Carrera 55 #35-25, Castilla', NULL, 3000, 6.2820, -75.5720, true)
                        ON CONFLICT DO NOTHING;
                    END IF;
                    
                    -- Cliente 7: Laura Sánchez - Santander
                    INSERT INTO customer (branch_id, name, phone1, phone2, active)
                    VALUES (branch_id_var, 'Laura Sánchez', '3207890123', NULL, true)
                    ON CONFLICT DO NOTHING
                    RETURNING id INTO customer7_id;
                    
                    IF customer7_id IS NULL THEN
                        SELECT id INTO customer7_id FROM customer WHERE branch_id = branch_id_var AND phone1 = '3207890123' LIMIT 1;
                    END IF;
                    
                    IF customer7_id IS NOT NULL AND santander_id IS NOT NULL THEN
                        INSERT INTO address (customer_id, neighborhood_id, address, additional_info, delivery_fee, latitude, longitude, is_primary)
                        VALUES (customer7_id, santander_id, 'Calle 75 #50-35, Santander', NULL, 4000, 6.2920, -75.5620, true)
                        ON CONFLICT DO NOTHING;
                    END IF;
                    
                    -- Cliente 8: Andrés López - Pedregal
                    INSERT INTO customer (branch_id, name, phone1, phone2, active)
                    VALUES (branch_id_var, 'Andrés López', '3158901234', NULL, true)
                    ON CONFLICT DO NOTHING
                    RETURNING id INTO customer8_id;
                    
                    IF customer8_id IS NULL THEN
                        SELECT id INTO customer8_id FROM customer WHERE branch_id = branch_id_var AND phone1 = '3158901234' LIMIT 1;
                    END IF;
                    
                    IF customer8_id IS NOT NULL AND pedregal_id IS NOT NULL THEN
                        INSERT INTO address (customer_id, neighborhood_id, address, additional_info, delivery_fee, latitude, longitude, is_primary)
                        VALUES (customer8_id, pedregal_id, 'Carrera 60 #40-20, Pedregal', NULL, 2500, 6.3020, -75.5520, true)
                        ON CONFLICT DO NOTHING;
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Eliminar datos iniciales (opcional, generalmente no se hace rollback de seed data)
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    branch_id_var INTEGER;
                BEGIN
                    SELECT id INTO branch_id_var FROM branch WHERE name = 'Santander' LIMIT 1;
                    
                    IF branch_id_var IS NOT NULL THEN
                        -- Eliminar direcciones y clientes
                        DELETE FROM address WHERE customer_id IN (SELECT id FROM customer WHERE branch_id = branch_id_var);
                        DELETE FROM customer WHERE branch_id = branch_id_var;
                        
                        -- Eliminar productos y categorías
                        DELETE FROM product WHERE category_id IN (SELECT id FROM product_category WHERE branch_id = branch_id_var);
                        DELETE FROM product_category WHERE branch_id = branch_id_var;
                        
                        -- Eliminar app
                        DELETE FROM app WHERE bank_id IN (SELECT id FROM bank WHERE branch_id = branch_id_var);
                        
                        -- Eliminar banco
                        DELETE FROM bank WHERE branch_id = branch_id_var;
                        
                        -- Eliminar barrios
                        DELETE FROM neighborhood WHERE branch_id = branch_id_var;
                        
                        -- Eliminar usuarios
                        DELETE FROM ""user"" WHERE branch_id = branch_id_var;
                        
                        -- Eliminar sucursal
                        DELETE FROM branch WHERE id = branch_id_var;
                    END IF;
                END $$;
            ");
        }
    }
}
