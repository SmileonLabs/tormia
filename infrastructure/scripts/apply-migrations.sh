#!/bin/sh
set -eu

echo "Waiting for PostgreSQL..."
until pg_isready -q; do
  sleep 1
done

psql -v ON_ERROR_STOP=1 <<'SQL'
CREATE TABLE IF NOT EXISTS platform_schema_migrations
(
    version     text PRIMARY KEY,
    applied_at  timestamptz NOT NULL DEFAULT now()
);
SQL

for migration in /migrations/*.sql; do
  version=$(basename "$migration")
  applied=$(psql -tA -v ON_ERROR_STOP=1 \
    -c "SELECT 1 FROM platform_schema_migrations WHERE version = '$version';")

  if [ "$applied" = "1" ]; then
    echo "Skipping already applied migration: $version"
    continue
  fi

  echo "Applying migration: $version"
  psql -v ON_ERROR_STOP=1 -f "$migration"
  psql -v ON_ERROR_STOP=1 \
    -c "INSERT INTO platform_schema_migrations (version) VALUES ('$version');"
done

echo "Database migrations are current."
