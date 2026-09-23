#!/bin/sh
set -eu

: "${DATABASE_URL:?DATABASE_URL is required}"
: "${BUCKET:?BUCKET is required}"
: "${ENDPOINT:?ENDPOINT is required}"
: "${AWS_ACCESS_KEY_ID:?AWS_ACCESS_KEY_ID is required}"
: "${AWS_SECRET_ACCESS_KEY:?AWS_SECRET_ACCESS_KEY is required}"

STAMP="$(date -u +%Y%m%d-%H%M%S)"
FILE="backup-${STAMP}.dump"
TMP="/tmp/${FILE}"

cleanup() {
  rm -f "$TMP"
}
trap cleanup EXIT INT TERM

echo "Starting PostgreSQL backup at ${STAMP} UTC..."

pg_dump "$DATABASE_URL" \
  --format=custom \
  --no-owner \
  --no-acl \
  --file="$TMP"

echo "Validating dump..."
pg_restore --list "$TMP" >/dev/null

echo "Uploading ${FILE} to bucket..."
if [ -n "${REGION:-}" ]; then
  aws s3 cp "$TMP" "s3://${BUCKET}/${FILE}" \
    --endpoint-url "$ENDPOINT" \
    --region "$REGION" \
    --only-show-errors
else
  aws s3 cp "$TMP" "s3://${BUCKET}/${FILE}" \
    --endpoint-url "$ENDPOINT" \
    --only-show-errors
fi

echo "Backup completed successfully: ${FILE}"
