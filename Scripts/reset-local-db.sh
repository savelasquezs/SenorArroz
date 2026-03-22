#!/usr/bin/env bash
# Reinicia la BD local y aplica local-init-completo.sql
#
# Uso (desde la carpeta SenorArroz/):
#   bash Scripts/reset-local-db.sh
#
# Contraseña (opcional, por defecto la de tu entorno local):
#   PGPASSWORD='tu_clave' bash Scripts/reset-local-db.sh
#
# Nombre de la BD (por defecto senorArroz, con comillas en PostgreSQL):
#   PGDATABASE_NAME='senorArroz' bash Scripts/reset-local-db.sh

set -euo pipefail

HOST="${PGHOST:-localhost}"
PORT="${PGPORT:-5433}"
USER="${PGUSER:-postgres}"
DB="${PGDATABASE_NAME:-senorArroz}"
export PGPASSWORD="${PGPASSWORD:-Santy1994.}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SQL_FILE="${SCRIPT_DIR}/local-init-completo.sql"

echo ">>> Cortando conexiones a \"${DB}\"..."
psql -h "$HOST" -p "$PORT" -U "$USER" -d postgres -v ON_ERROR_STOP=1 -c \
  "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '${DB}' AND pid <> pg_backend_pid();" \
  || true

echo ">>> DROP DATABASE IF EXISTS \"${DB}\"..."
psql -h "$HOST" -p "$PORT" -U "$USER" -d postgres -v ON_ERROR_STOP=1 \
  -c "DROP DATABASE IF EXISTS \"${DB}\";"

echo ">>> CREATE DATABASE \"${DB}\"..."
psql -h "$HOST" -p "$PORT" -U "$USER" -d postgres -v ON_ERROR_STOP=1 \
  -c "CREATE DATABASE \"${DB}\";"

echo ">>> Aplicando ${SQL_FILE}..."
psql -h "$HOST" -p "$PORT" -U "$USER" -d "$DB" -v ON_ERROR_STOP=1 -f "$SQL_FILE"

echo ">>> Listo. Base \"${DB}\" reconstruida."
