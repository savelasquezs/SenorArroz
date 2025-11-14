-- Ver datos del usuario Santiago
SELECT 
    id, 
    name, 
    email, 
    role, 
    active, 
    branch_id,
    (SELECT name FROM branch WHERE id = "user".branch_id) as branch_name,
    phone,
    created_at,
    updated_at
FROM "user" 
WHERE email = 'santyvano@outlook.com';

