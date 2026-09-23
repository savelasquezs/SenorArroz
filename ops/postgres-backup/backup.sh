#!/bin/sh
set -eu

: "${DATABASE_URL:?DATABASE_URL is required}"
: "${BUCKET:?BUCKET is required}"
: "${ENDPOINT:?ENDPOINT is required}"
: "${AWS_ACCESS_KEY_ID:?AWS_ACCESS_KEY_ID is required}"
: "${AWS_SECRET_ACCESS_KEY:?AWS_SECRET_ACCESS_KEY is required}"

RETENTION_DAYS="${RETENTION_DAYS:-90}"
R2_RETENTION_DAYS="${R2_RETENTION_DAYS:-84}"

for value_name in RETENTION_DAYS R2_RETENTION_DAYS; do
  eval value="\${$value_name}"
  case "$value" in
    ''|*[!0-9]*)
      echo "$value_name must be a non-negative integer." >&2
      exit 1
      ;;
  esac
done

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

r2_configured() {
  [ -n "${R2_BUCKET:-}" ] &&
  [ -n "${R2_ENDPOINT:-}" ] &&
  [ -n "${R2_ACCESS_KEY_ID:-}" ] &&
  [ -n "${R2_SECRET_ACCESS_KEY:-}" ]
}

r2_cp() {
  AWS_ACCESS_KEY_ID="$R2_ACCESS_KEY_ID" \
  AWS_SECRET_ACCESS_KEY="$R2_SECRET_ACCESS_KEY" \
  aws s3 cp "$1" "$2" \
    --endpoint-url "$R2_ENDPOINT" \
    --region "${R2_REGION:-auto}" \
    --only-show-errors
}

r2api() {
  AWS_ACCESS_KEY_ID="$R2_ACCESS_KEY_ID" \
  AWS_SECRET_ACCESS_KEY="$R2_SECRET_ACCESS_KEY" \
  aws s3api "$@" \
    --endpoint-url "$R2_ENDPOINT" \
    --region "${R2_REGION:-auto}"
}

echo "Starting PostgreSQL backup at ${STAMP} UTC..."

pg_dump "$DATABASE_URL" \
  --format=custom \
  --no-owner \
  --no-acl \
  --file="$TMP"

echo "Validating dump..."
pg_restore --list "$TMP" >/dev/null

echo "Uploading ${FILE} to Railway bucket..."
s3_cp "$TMP" "s3://${BUCKET}/${FILE}"

echo "Railway backup completed successfully: ${FILE}"

if [ "$RETENTION_DAYS" -gt 0 ]; then
  CUTOFF="$(date -u -d "${RETENTION_DAYS} days ago" +%Y-%m-%dT%H:%M:%SZ)"
  echo "Removing Railway backup objects older than ${RETENTION_DAYS} days (before ${CUTOFF})..."

  OLD_KEYS="$(s3api list-objects-v2 \
    --bucket "$BUCKET" \
    --prefix "backup-" \
    --query "Contents[?LastModified<=\`${CUTOFF}\`].Key" \
    --output text)"

  for key in $OLD_KEYS; do
    [ "$key" = "None" ] && continue
    case "$key" in
      backup-*.dump)
        echo "Deleting expired Railway backup: $key"
        s3api delete-object --bucket "$BUCKET" --key "$key" >/dev/null
        ;;
    esac
  done
fi

DAY_OF_WEEK="$(date -u +%u)"
if r2_configured && { [ "$DAY_OF_WEEK" = "7" ] || [ "${R2_FORCE_BACKUP:-0}" = "1" ]; }; then
  R2_KEY="weekly/${FILE}"
  echo "Uploading weekly external backup to Cloudflare R2: ${R2_KEY}"
  r2_cp "$TMP" "s3://${R2_BUCKET}/${R2_KEY}"
  echo "Cloudflare R2 backup completed successfully: ${R2_KEY}"

  if [ "$R2_RETENTION_DAYS" -gt 0 ]; then
    R2_CUTOFF="$(date -u -d "${R2_RETENTION_DAYS} days ago" +%Y-%m-%dT%H:%M:%SZ)"
    echo "Removing R2 weekly backups older than ${R2_RETENTION_DAYS} days (before ${R2_CUTOFF})..."

    R2_OLD_KEYS="$(r2api list-objects-v2 \
      --bucket "$R2_BUCKET" \
      --prefix "weekly/" \
      --query "Contents[?LastModified<=\`${R2_CUTOFF}\`].Key" \
      --output text)"

    for key in $R2_OLD_KEYS; do
      [ "$key" = "None" ] && continue
      case "$key" in
        weekly/backup-*.dump)
          echo "Deleting expired R2 backup: $key"
          r2api delete-object --bucket "$R2_BUCKET" --key "$key" >/dev/null
          ;;
      esac
    done
  fi
elif r2_configured; then
  echo "External R2 backup skipped today; weekly copy runs on Sundays."
else
  echo "External R2 backup not configured; Railway backup only."
fi

echo "Backup job finished."
