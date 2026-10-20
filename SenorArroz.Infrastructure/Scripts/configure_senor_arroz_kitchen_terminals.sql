-- Clasificación autorizada de las dos pantallas existentes de Señor Arroz.
-- Ejecutar después de add_employee_leave_requests.sql y antes de desplegar la API.
-- No cambia contraseñas, sesiones, active, nómina ni acceso web de las personas.
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '60s';
SELECT set_config('app.current_tenant_id', '1', true);
LOCK TABLE public."user" IN SHARE ROW EXCLUSIVE MODE;
CREATE TEMP TABLE confirmed_kitchen_terminals (
    user_id integer PRIMARY KEY, branch_id integer NOT NULL, email text NOT NULL UNIQUE
) ON COMMIT DROP;
INSERT INTO confirmed_kitchen_terminals VALUES
    (5, 1, 'juan@juan.com'),
    (10, 2, 'pao.villorina@hotmail.com');
DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM public.tenant WHERE id = 1 AND slug = 'senor-arroz') THEN
        RAISE EXCEPTION 'Esta clasificación corresponde exclusivamente al tenant Señor Arroz (1).';
    END IF;
    IF EXISTS (SELECT 1 FROM confirmed_kitchen_terminals c LEFT JOIN public."user" u
        ON u.tenant_id = 1 AND u.id = c.user_id AND u.branch_id = c.branch_id
            AND lower(btrim(u.email)) = c.email
        WHERE u.id IS NULL OR u.role <> 'kitchen' OR NOT u.active OR NOT u.web_access_enabled)
        OR EXISTS (SELECT 1 FROM confirmed_kitchen_terminals c WHERE
            (SELECT count(*) FROM public."user" u WHERE u.tenant_id = 1 AND lower(btrim(u.email)) = c.email) <> 1) THEN
        RAISE EXCEPTION 'Las terminales no coinciden con los IDs, correos, sucursales y accesos verificados. No se cambió ninguna cuenta.';
    END IF;
    IF EXISTS (SELECT 1 FROM confirmed_kitchen_terminals c JOIN public."user" u
        ON u.tenant_id = 1 AND u.id = c.user_id
        WHERE u.payroll_expense_id IS NOT NULL OR u.delivery_app_enabled
            OR EXISTS (SELECT 1 FROM public.employee_lunch l WHERE l.tenant_id = u.tenant_id AND l.user_id = u.id)
            OR EXISTS (SELECT 1 FROM public.employee_leave_request r WHERE r.tenant_id = u.tenant_id AND r.user_id = u.id)) THEN
        RAISE EXCEPTION 'Una cuenta con nómina, domicilios, almuerzos o permisos propios no puede convertirse en terminal.';
    END IF;
END $$;
UPDATE public."user" u SET account_type = 'terminal'
FROM confirmed_kitchen_terminals c
WHERE u.tenant_id = 1 AND u.id = c.user_id AND u.account_type IS DISTINCT FROM 'terminal';
SELECT u.id, u.branch_id, u.email, u.account_type, u.active, u.web_access_enabled
FROM public."user" u JOIN confirmed_kitchen_terminals c ON c.user_id = u.id
WHERE u.tenant_id = 1 ORDER BY u.id;
COMMIT;
