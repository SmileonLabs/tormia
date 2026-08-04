#!/bin/sh
set -eu

root=${1:-/opt/tormia-test}
set -a
. "$root/secrets/bootstrap.env"
set +a

docker run --rm \
  -e "PGHOST=$PGHOST" \
  -e "PGPORT=$PGPORT" \
  -e "PGDATABASE=$PGDATABASE" \
  -e "PGUSER=$PGAPPUSER" \
  -e "PGPASSWORD=$PGAPPPASSWORD" \
  -v "$root/migrations:/migrations:ro" \
  -v "$root/apply-migrations.sh:/scripts/apply-migrations.sh:ro" \
  postgres:16-alpine \
  /bin/sh /scripts/apply-migrations.sh
