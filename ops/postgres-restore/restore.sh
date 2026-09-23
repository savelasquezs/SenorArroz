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

LATEST_KEY="$(s3api list-objects-v2 \
  --bucket "$BUCKET" \
  --prefix "backup-" \
  --query 'reverse(sort_by(Contents,&LastModified))[0].Key' \
  --output text)"

case "$LATEST_KEY" in
  backup-*.dump) ;;
  *)
    echo "No valid backup-*.dump object found in bucket." >&2
    exit 1
    ;;
esac

TMP="/tmp/restore.dump"
cleanup() {
  rm -f "$TMP"
}
trap cleanup EXIT INT TERM

echo "Downloading latest backup: ${LATEST_KEY}"
s3_cp "s3://${BUCKET}/${LATEST_KEY}" "$TMP"

echo "Validating dump..."
pg_restore --list "$TMP" >/dev/null

echo "Restoring into empty test database..."
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

echo "Restore verification completed successfully from ${LATEST_KEY}."
