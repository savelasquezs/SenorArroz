#!/bin/sh
set -eu

: "${OWNER_DATABASE_URL:?OWNER_DATABASE_URL is required}"
: "${RUNTIME_DATABASE_URL:?RUNTIME_DATABASE_URL is required}"
: "${RUNTIME_PASSWORD:?RUNTIME_PASSWORD is required}"

RUNTIME_ROLE="${RUNTIME_ROLE:-senorarroz_runtime}"

case "$RUNTIME_ROLE" in
  [A-Za-z_]* ) ;;
  *)
    echo "RUNTIME_ROLE must start with a letter or underscore." >&2
    exit 1
    ;;
esac
case "$RUNTIME_ROLE" in
  *[!A-Za-z0-9_]* )
    echo "RUNTIME_ROLE may contain only letters, numbers and underscores." >&2
    exit 1
    ;;
esac

case "$RUNTIME_PASSWORD" in
  *[!A-Za-z0-9]*|'')
    echo "RUNTIME_PASSWORD must be a non-empty alphanumeric value." >&2
    exit 1
    ;;
esac

SCRIPT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
REPO_ROOT="$(CDPATH= cd -- "${SCRIPT_DIR}/../.." && pwd)"
VERIFY_SCRIPT="${REPO_ROOT}/SenorArroz.Infrastructure/Scripts/verify_multitenant_runtime_role.sql"

if [ ! -f "$VERIFY_SCRIPT" ]; then
  echo "Runtime verification script not found: ${VERIFY_SCRIPT}" >&2
  exit 1
fi

echo "Creating or hardening PostgreSQL runtime role ${RUNTIME_ROLE}..."
ROLE_EXISTS="$(psql "$OWNER_DATABASE_URL" -v ON_ERROR_STOP=1 -Atqc "SELECT 1 FROM pg_roles WHERE rolname = '${RUNTIME_ROLE}'")"

if [ "$ROLE_EXISTS" = "1" ]; then
  psql "$OWNER_DATABASE_URL" -v ON_ERROR_STOP=1 -c \
    "ALTER ROLE \"${RUNTIME_ROLE}\" WITH LOGIN PASSWORD '${RUNTIME_PASSWORD}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;" >/dev/null
else
  psql "$OWNER_DATABASE_URL" -v ON_ERROR_STOP=1 -c \
    "CREATE ROLE \"${RUNTIME_ROLE}\" WITH LOGIN PASSWORD '${RUNTIME_PASSWORD}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;" >/dev/null
fi

psql "$OWNER_DATABASE_URL" -v ON_ERROR_STOP=1 -v runtime_role="$RUNTIME_ROLE" <<'SQL' >/dev/null
SELECT format('GRANT CONNECT ON DATABASE %I TO %I', current_database(), :'runtime_role') \gexec
SELECT format('GRANT USAGE ON SCHEMA public TO %I', :'runtime_role') \gexec
SELECT format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO %I', :'runtime_role') \gexec
SELECT format('GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO %I', :'runtime_role') \gexec
SELECT format('ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO %I', :'runtime_role') \gexec
SELECT format('ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO %I', :'runtime_role') \gexec
SELECT format('GRANT USAGE ON SCHEMA app TO %I', :'runtime_role')
WHERE to_regnamespace('app') IS NOT NULL \gexec
SELECT format('GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA app TO %I', :'runtime_role')
WHERE to_regnamespace('app') IS NOT NULL \gexec
SELECT format('ALTER DEFAULT PRIVILEGES IN SCHEMA app GRANT EXECUTE ON FUNCTIONS TO %I', :'runtime_role')
WHERE to_regnamespace('app') IS NOT NULL \gexec
SQL

echo "Verifying runtime role and forced tenant RLS..."
psql "$RUNTIME_DATABASE_URL" -v ON_ERROR_STOP=1 -f "$VERIFY_SCRIPT"

echo "Runtime role verification completed successfully."
