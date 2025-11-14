-- Insertar o actualizar usuario Santiago
-- Este script es idempotente: puede ejecutarse múltiples veces

-- Primero, obtener el branch_id de Santander (o usar el ID 1 si no existe)
DO $$
DECLARE
    branch_id_var INTEGER;
    user_exists BOOLEAN;
BEGIN
    -- Obtener el ID de la sucursal Santander
    SELECT id INTO branch_id_var FROM branch WHERE name = 'Santander' LIMIT 1;
    
    -- Si no existe, usar el primer branch disponible o crear uno
    IF branch_id_var IS NULL THEN
        SELECT COALESCE(MAX(id), 1) INTO branch_id_var FROM branch;
    END IF;

    -- Verificar si el usuario ya existe
    SELECT EXISTS(
        SELECT 1 FROM "user" WHERE email = 'santyvano@outlook.com'
    ) INTO user_exists;

    IF user_exists THEN
        -- Actualizar usuario existente
        UPDATE "user" 
        SET 
            branch_id = branch_id_var,
            role = 'superadmin',
            name = 'Santiago',
            phone = '0000000000',
            password_hash = '$2a$11$5ERmxaaUoW.MLkirB/l22eyaZxF1OCK2kjtN9DtYo14k.o2XmkyBy',
            active = true
        WHERE email = 'santyvano@outlook.com';
        
        RAISE NOTICE 'Usuario Santiago actualizado (ID: %)', (SELECT id FROM "user" WHERE email = 'santyvano@outlook.com');
    ELSE
        -- Insertar nuevo usuario
        INSERT INTO "user" (branch_id, role, name, email, phone, password_hash, active)
        VALUES (
            branch_id_var,
            'superadmin',
            'Santiago',
            'santyvano@outlook.com',
            '0000000000',
            '$2a$11$5ERmxaaUoW.MLkirB/l22eyaZxF1OCK2kjtN9DtYo14k.o2XmkyBy',
            true
        );
        
        RAISE NOTICE 'Usuario Santiago creado (ID: %)', (SELECT id FROM "user" WHERE email = 'santyvano@outlook.com');
    END IF;
END $$;

-- Verificar el resultado
SELECT id, name, email, role, active, branch_id 
FROM "user" 
WHERE email = 'santyvano@outlook.com';

