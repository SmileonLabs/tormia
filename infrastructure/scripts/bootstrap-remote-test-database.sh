#!/bin/sh
set -eu

root=${1:-/opt/tormia-test}
set -a
. "$root/secrets/bootstrap.env"
set +a

docker run --rm \
  -e "PGPASSWORD=$PGADMINPASSWORD" \
  -v "$root/bootstrap-test-database.sql:/bootstrap.sql:ro" \
  postgres:16-alpine \
  psql \
    -h "$PGHOST" \
    -p "$PGPORT" \
    -U "$PGADMINUSER" \
    -d postgres \
    -v "app_user=$PGAPPUSER" \
    -v "app_password=$PGAPPPASSWORD" \
    -v "app_database=$PGDATABASE" \
    -f /bootstrap.sql
