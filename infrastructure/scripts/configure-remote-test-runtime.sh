#!/usr/bin/env sh
set -eu

ROOT_DIR="${1:-/opt/tormia-test}"
BOOTSTRAP_FILE="$ROOT_DIR/secrets/bootstrap.env"
RUNTIME_FILE="$ROOT_DIR/secrets/world-authority.env"

if [ ! -f "$BOOTSTRAP_FILE" ]; then
  echo "Missing bootstrap environment file: $BOOTSTRAP_FILE" >&2
  exit 1
fi

set -a
. "$BOOTSTRAP_FILE"
set +a

umask 077
{
  printf '%s\n' 'ASPNETCORE_ENVIRONMENT=Production'
  printf '%s\n' 'ASPNETCORE_URLS=http://+:8080'
  printf 'ConnectionStrings__Platform=Host=%s;Port=%s;Database=%s;Username=%s;Password=%s;Pooling=true;Maximum Pool Size=100;SSL Mode=Require;Trust Server Certificate=true\n' \
    "$PGHOST" "$PGPORT" "$PGDATABASE" "$PGAPPUSER" "$PGAPPPASSWORD"
  printf 'ConnectionStrings__RealtimeRedis=%s:%s,abortConnect=false\n' "$REDISHOST" "$REDISPORT"
} > "$RUNTIME_FILE"

chmod 600 "$RUNTIME_FILE"
echo "Runtime environment configured at $RUNTIME_FILE"
