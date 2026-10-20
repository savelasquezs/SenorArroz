-- Ejecutar SOLO con el inventario revisado de cuentas de UN tenant.
-- psql: \set employee_tenant_id 1
-- psql: \set employee_accounts_json '[{"user_id":123,"account_type":"personal","web_access_enabled":true},{"user_id":456,"account_type":"terminal","web_access_enabled":true}]'
-- Los IDs son ejemplos: no ejecutar sin reemplazarlos. No se infiere por nombre ni por acceso web.
\set ON_ERROR_STOP on
\if :{?employee_tenant_id}
\else
  \echo 'Falta employee_tenant_id. No se cambió ninguna cuenta.'
  \quit 1
\endif
\if :{?employee_accounts_json}
\else
  \echo 'Falta employee_accounts_json. No se cambió ninguna cuenta.'
  \quit 1
\endif
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '60s';
SELECT set_config('app.current_tenant_id', :'employee_tenant_id', true);
CREATE TEMP TABLE employee_account_choices ON COMMIT DROP AS
SELECT * FROM jsonb_to_recordset(:'employee_accounts_json'::jsonb)
    AS x(user_id integer, account_type text, web_access_enabled boolean);
ALTER TABLE employee_account_choices ADD PRIMARY KEY(user_id);
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM employee_account_choices c LEFT JOIN public."user" u
        ON u.id = c.user_id AND u.tenant_id = app.current_tenant_id()
        WHERE u.id IS NULL OR u.role <> 'kitchen' OR c.account_type IS NULL
            OR c.account_type NOT IN ('personal','terminal') OR c.web_access_enabled IS NULL) THEN
        RAISE EXCEPTION 'El inventario contiene cuentas inválidas, otro tenant o campos incompletos.';
    END IF;
    IF EXISTS (SELECT 1 FROM public."user" u WHERE u.tenant_id = app.current_tenant_id()
        AND u.role = 'kitchen' AND NOT EXISTS (SELECT 1 FROM employee_account_choices c WHERE c.user_id = u.id)) THEN
        RAISE EXCEPTION 'Se debe clasificar explícitamente cada cuenta Kitchen del tenant, incluidas las inactivas.';
    END IF;
    IF EXISTS (SELECT 1 FROM employee_account_choices c JOIN public."user" u ON u.id = c.user_id
        WHERE c.account_type = 'terminal' AND (u.payroll_expense_id IS NOT NULL OR u.delivery_app_enabled
            OR EXISTS (SELECT 1 FROM public.employee_lunch l WHERE l.tenant_id = u.tenant_id AND l.user_id = u.id)
            OR EXISTS (SELECT 1 FROM public.employee_leave_request r WHERE r.tenant_id = u.tenant_id AND r.user_id = u.id))) THEN
        RAISE EXCEPTION 'Una persona con nómina, almuerzos o permisos no puede convertirse en terminal. Cree una cuenta técnica separada y conserve al empleado.';
    END IF;
END $$;
UPDATE public."user" u SET account_type = c.account_type, web_access_enabled = c.web_access_enabled
FROM employee_account_choices c WHERE u.id = c.user_id AND u.tenant_id = app.current_tenant_id();
-- active, contraseña, ID, rol y referencias históricas permanecen intactos.
SELECT u.id, u.name, u.account_type, u.active, u.web_access_enabled
FROM public."user" u WHERE u.tenant_id = app.current_tenant_id() AND u.role = 'kitchen' ORDER BY u.branch_id, u.id;
COMMIT;
