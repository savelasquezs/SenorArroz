#!/bin/sh
set -eu

: "${DATABASE_URL:?DATABASE_URL is required}"
: "${BUCKET:?BUCKET is required}"
: "${ENDPOINT:?ENDPOINT is required}"
: "${AWS_ACCESS_KEY_ID:?AWS_ACCESS_KEY_ID is required}"
: "${AWS_SECRET_ACCESS_KEY:?AWS_SECRET_ACCESS_KEY is required}"

RETENTION_DAYS="${RETENTION_DAYS:-90}"
case "$RETENTION_DAYS" in
  ''|*[!0-9]*)
    echo "RETENTION_DAYS must be a non-negative integer." >&2
    exit 1
    ;;
esac

STAMP="$(date -u +%Y%m%d-%H%M%S)"
FILE="backup-${STAMP}.dump"
TMP="/tmp/${FILE}"

cleanup() {
  rm -f "$TMP"
}
trap cleanup EXIT INT TERM

s3_cp() {
  if [ -n "${REGION:-}" ]; then
    aws s3 cp "$1" "$2" \
      --endpoint-url "$ENDPOINT" \
      --region "$REGION" \
      --only-show-errors
  else
    aws s3 cp "$1" "$2" \
      --endpoint-url "$ENDPOINT" \
      --only-show-errors
  fi
}

s3api() {
  if [ -n "${REGION:-}" ]; then
    aws s3api "$@" --endpoint-url "$ENDPOINT" --region "$REGION"
  else
    aws s3api "$@" --endpoint-url "$ENDPOINT"
  fi
}

echo "Starting PostgreSQL backup at ${STAMP} UTC..."

pg_dump "$DATABASE_URL" \
  --format=custom \
  --no-owner \
  --no-acl \
  --file="$TMP"

echo "Validating dump..."
pg_restore --list "$TMP" >/dev/null

echo "Uploading ${FILE} to bucket..."
s3_cp "$TMP" "s3://${BUCKET}/${FILE}"

echo "Backup completed successfully: ${FILE}"

if [ "$RETENTION_DAYS" -gt 0 ]; then
  CUTOFF="$(date -u -d "${RETENTION_DAYS} days ago" +%Y-%m-%dT%H:%M:%SZ)"
  echo "Removing backup objects older than ${RETENTION_DAYS} days (before ${CUTOFF})..."

  OLD_KEYS="$(s3api list-objects-v2 \
    --bucket "$BUCKET" \
    --prefix "backup-" \
    --query "Contents[?LastModified<=\`${CUTOFF}\`].Key" \
    --output text)"

  for key in $OLD_KEYS; do
    [ "$key" = "None" ] && continue
    case "$key" in
      backup-*.dump)
        echo "Deleting expired backup: $key"
        s3api delete-object --bucket "$BUCKET" --key "$key" >/dev/null
        ;;
    esac
  done
fi

echo "Backup job finished."
