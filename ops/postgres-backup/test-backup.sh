#!/bin/sh
set -eu
ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)"
SCRIPT="$ROOT/ops/postgres-backup/backup.sh"
TEST_TMP="$(mktemp -d)"
trap 'rm -rf "$TEST_TMP"' EXIT HUP INT TERM
mkdir -p "$TEST_TMP/bin"
export TEST_AWS_LOG="$TEST_TMP/aws.log"

cat > "$TEST_TMP/bin/pg_dump" <<'MOCK'
#!/bin/sh
set -eu
for arg in "$@"; do
  case "$arg" in
    --file=*) output="$(printf '%s' "$arg" | sed 's/^--file=//')" ;;
  esac
done
printf 'fake dump\n' > "$output"
MOCK
cat > "$TEST_TMP/bin/pg_restore" <<'MOCK'
#!/bin/sh
exit 0
MOCK
cat > "$TEST_TMP/bin/aws" <<'MOCK'
#!/bin/sh
set -eu
printf '%s\n' "$*" >> "$TEST_AWS_LOG"
if [ "$1" = "s3" ] && [ "$2" = "cp" ]; then
  case "$4" in
    s3://external/*)
      if [ "$FAIL_R2" = "1" ]; then exit 42; fi ;;
  esac
fi
MOCK
chmod +x "$TEST_TMP/bin/"*

run_case() {
  name="$1"; expected="$2"; copies="$3"
  shift 3
  : > "$TEST_AWS_LOG"
  status=0
  env -i PATH="$TEST_TMP/bin:$PATH" TEST_AWS_LOG="$TEST_AWS_LOG" \
    DATABASE_URL="postgres://dummy.invalid/test" \
    BUCKET=railway ENDPOINT="https://dummy.invalid" \
    AWS_ACCESS_KEY_ID=fake AWS_SECRET_ACCESS_KEY=fake \
    RETENTION_DAYS=0 R2_RETENTION_DAYS=0 FAIL_R2=0 \
    "$@" /bin/sh "$SCRIPT" > "$TEST_TMP/output" 2>&1 || status=$?
  count="$(grep -c '^s3 cp ' "$TEST_AWS_LOG" || true)"
  if [ "$expected" = ok ] && [ "$status" -ne 0 ]; then
    echo "FAIL $name: expected success, got $status" >&2
    cat "$TEST_TMP/output" >&2
    exit 1
  fi
  if [ "$expected" = fail ] && [ "$status" -eq 0 ]; then
    echo "FAIL $name: expected failure" >&2
    exit 1
  fi
  if [ "$count" -ne "$copies" ]; then
    echo "FAIL $name: expected $copies uploads, got $count" >&2
    exit 1
  fi
  if [ "$expected" = fail ] && ! grep -q 'Railway backup completed successfully' "$TEST_TMP/output"; then
    echo "FAIL $name: local backup was not preserved" >&2
    exit 1
  fi
  echo "PASS $name"
}

run_case railway-only ok 1
run_case partial-r2 fail 1 R2_BUCKET=external
run_case forced-without-r2 fail 1 R2_FORCE_BACKUP=1
run_case complete-r2 ok 2 R2_BUCKET=external R2_ENDPOINT=https://r2.invalid R2_ACCESS_KEY_ID=fake R2_SECRET_ACCESS_KEY=fake R2_FORCE_BACKUP=1
run_case r2-upload-error fail 2 R2_BUCKET=external R2_ENDPOINT=https://r2.invalid R2_ACCESS_KEY_ID=fake R2_SECRET_ACCESS_KEY=fake R2_FORCE_BACKUP=1 FAIL_R2=1
