#!/usr/bin/env sh
set -eu

ROOT_DIR="${1:-/opt/tormia-test}"
SECRETS_FILE="$ROOT_DIR/secrets/bootstrap.env"

if [ ! -f "$SECRETS_FILE" ]; then
  echo "Missing bootstrap environment file: $SECRETS_FILE" >&2
  exit 1
fi

set -a
. "$SECRETS_FILE"
set +a

docker run --rm \
  -e PGPASSWORD="$PGAPPPASSWORD" \
  postgres:16-alpine \
  psql \
    -h "$PGHOST" \
    -p "$PGPORT" \
    -U "$PGAPPUSER" \
    -d "$PGDATABASE" \
    -tA \
    -c 'SELECT count(*) FROM platform_schema_migrations;'
