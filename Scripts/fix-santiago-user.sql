-- Limpiar y crear usuario Santiago correctamente
-- Elimina duplicados y crea/actualiza el usuario correcto

DO $$
DECLARE
    branch_id_var INTEGER;
    user_id_var INTEGER;
BEGIN
    -- Obtener el ID de la sucursal Santander
    SELECT id INTO branch_id_var FROM branch WHERE name = 'Santander' LIMIT 1;
    
    IF branch_id_var IS NULL THEN
        RAISE EXCEPTION 'No se encontró la sucursal Santander';
    END IF;

    -- Eliminar todos los usuarios con ese email (limpiar duplicados)
    DELETE FROM "user" WHERE email = 'santyvano@outlook.com';
    
    RAISE NOTICE 'Usuarios duplicados eliminados';

    -- Insertar usuario Santiago correcto
    INSERT INTO "user" (branch_id, role, name, email, phone, password_hash, active)
    VALUES (
        branch_id_var,
        'superadmin',
        'Santiago',
        'santyvano@outlook.com',
        '0000000000',
        '$2a$11$5ERmxaaUoW.MLkirB/l22eyaZxF1OCK2kjtN9DtYo14k.o2XmkyBy',
        true
    )
    RETURNING id INTO user_id_var;
    
    RAISE NOTICE 'Usuario Santiago creado correctamente (ID: %, Branch: %)', user_id_var, branch_id_var;
END $$;

-- Verificar el resultado
SELECT 
    id, 
    name, 
    email, 
    role, 
    active, 
    branch_id,
    (SELECT name FROM branch WHERE id = "user".branch_id) as branch_name
FROM "user" 
WHERE email = 'santyvano@outlook.com';

