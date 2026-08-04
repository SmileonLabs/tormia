#!/usr/bin/env sh
set -eu

CONTAINER_NAME="${TORMIA_TEST_CONTAINER_NAME:-tormia-test-authority}"
BACKUP_NAME="$CONTAINER_NAME-rollback-stage13"
FAILED_NAME="$CONTAINER_NAME-udp-failed-$(date -u +%Y%m%dT%H%M%SZ)"
HTTP_PORT="${TORMIA_TEST_HTTP_PORT:-5272}"

if ! docker ps -a --format '{{.Names}}' | grep -Fxq "$BACKUP_NAME"; then
  echo "Retained rollback container is missing: $BACKUP_NAME" >&2
  exit 1
fi
if ! docker ps -a --format '{{.Names}}' | grep -Fxq "$CONTAINER_NAME"; then
  echo "Current Authority container is missing: $CONTAINER_NAME" >&2
  exit 1
fi

docker stop "$CONTAINER_NAME" >/dev/null
docker rename "$CONTAINER_NAME" "$FAILED_NAME"
docker rename "$BACKUP_NAME" "$CONTAINER_NAME"
docker start "$CONTAINER_NAME" >/dev/null

attempts=0
until curl --fail --silent --show-error "http://127.0.0.1:$HTTP_PORT/health" >/dev/null; do
  attempts=$((attempts + 1))
  if [ "$attempts" -ge 60 ]; then
    echo "Rollback container did not become healthy; failed UDP container is retained as $FAILED_NAME." >&2
    exit 1
  fi
  sleep 1
done

echo "Rollback restored. Failed UDP container retained as $FAILED_NAME."
