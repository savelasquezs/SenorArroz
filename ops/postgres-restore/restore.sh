#!/bin/sh
set -eu

: "${RESTORE_DATABASE_URL:?RESTORE_DATABASE_URL is required}"
: "${BUCKET:?BUCKET is required}"
: "${ENDPOINT:?ENDPOINT is required}"
: "${AWS_ACCESS_KEY_ID:?AWS_ACCESS_KEY_ID is required}"
: "${AWS_SECRET_ACCESS_KEY:?AWS_SECRET_ACCESS_KEY is required}"
: "${ALLOW_RESTORE:?ALLOW_RESTORE must be set to YES}"

if [ "$ALLOW_RESTORE" != "YES" ]; then
  echo "Refusing restore: ALLOW_RESTORE must be exactly YES." >&2
  exit 1
fi

BACKUP_PREFIX="${BACKUP_PREFIX:-backup-}"
RESTORE_BACKUP_KEY="${RESTORE_BACKUP_KEY:-}"

if [ -z "$BACKUP_PREFIX" ]; then
  echo "BACKUP_PREFIX cannot be empty." >&2
  exit 1
fi

s3api() {
  if [ -n "${REGION:-}" ]; then
    aws s3api "$@" --endpoint-url "$ENDPOINT" --region "$REGION"
  else
    aws s3api "$@" --endpoint-url "$ENDPOINT"
  fi
}

s3_cp() {
  if [ -n "${REGION:-}" ]; then
    aws s3 cp "$1" "$2" --endpoint-url "$ENDPOINT" --region "$REGION" --only-show-errors
  else
    aws s3 cp "$1" "$2" --endpoint-url "$ENDPOINT" --only-show-errors
  fi
}

echo "Checking that restore target is empty..."
USER_TABLES="$(psql "$RESTORE_DATABASE_URL" -v ON_ERROR_STOP=1 -Atqc "
  SELECT count(*)
  FROM pg_catalog.pg_class c
  JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
  WHERE c.relkind IN ('r','p')
    AND n.nspname NOT IN ('pg_catalog','information_schema')
    AND n.nspname !~ '^pg_toast';
")"

if [ "$USER_TABLES" != "0" ]; then
  echo "Refusing restore: target database is not empty (${USER_TABLES} user tables found)." >&2
  exit 1
fi

if [ -n "$RESTORE_BACKUP_KEY" ]; then
  case "$RESTORE_BACKUP_KEY" in
    "${BACKUP_PREFIX}"*.dump) ;;
    *)
      echo "Refusing restore: RESTORE_BACKUP_KEY must match ${BACKUP_PREFIX}*.dump." >&2
      exit 1
      ;;
  esac

  echo "Checking requested backup object: ${RESTORE_BACKUP_KEY}"
  if ! s3api head-object --bucket "$BUCKET" --key "$RESTORE_BACKUP_KEY" >/dev/null 2>&1; then
    echo "Requested backup object was not found: ${RESTORE_BACKUP_KEY}" >&2
    exit 1
  fi
  BACKUP_KEY="$RESTORE_BACKUP_KEY"
else
  BACKUP_KEY="$(s3api list-objects-v2 \
    --bucket "$BUCKET" \
    --prefix "$BACKUP_PREFIX" \
    --query 'reverse(sort_by(Contents,&LastModified))[0].Key' \
    --output text)"

  case "$BACKUP_KEY" in
    "${BACKUP_PREFIX}"*.dump) ;;
    *)
      echo "No valid ${BACKUP_PREFIX}*.dump object found in bucket." >&2
      exit 1
      ;;
  esac
fi

TMP="/tmp/restore.dump"
cleanup() {
  rm -f "$TMP"
}
trap cleanup EXIT INT TERM

echo "Downloading backup: ${BACKUP_KEY}"
s3_cp "s3://${BUCKET}/${BACKUP_KEY}" "$TMP"

echo "Validating dump..."
pg_restore --list "$TMP" >/dev/null

echo "Restoring into empty recovery database..."
pg_restore \
  --dbname="$RESTORE_DATABASE_URL" \
  --no-owner \
  --no-acl \
  --single-transaction \
  --exit-on-error \
  "$TMP"

echo "Restore completed. Running sanity checks..."
psql "$RESTORE_DATABASE_URL" -v ON_ERROR_STOP=1 -P pager=off -c "
  SELECT 'tenant' AS entity, count(*) AS rows FROM public.tenant
  UNION ALL
  SELECT 'branch', count(*) FROM public.branch
  UNION ALL
  SELECT 'customer', count(*) FROM public.customer
  UNION ALL
  SELECT 'order', count(*) FROM public.\"order\";
"

echo "RESTORED_BACKUP_KEY=${BACKUP_KEY}"
echo "Restore verification completed successfully from ${BACKUP_KEY}."
