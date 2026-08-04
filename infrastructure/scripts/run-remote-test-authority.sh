#!/usr/bin/env sh
set -eu

ROOT_DIR="${1:-/opt/tormia-test}"
CONTAINER_NAME="tormia-test-authority"
IMAGE_NAME="tormia-world-authority:test-20260803"
RUNTIME_FILE="$ROOT_DIR/secrets/world-authority.env"

if [ ! -f "$RUNTIME_FILE" ]; then
  echo "Missing runtime environment file: $RUNTIME_FILE" >&2
  exit 1
fi

if docker ps -a --format '{{.Names}}' | grep -Fxq "$CONTAINER_NAME"; then
  docker rm -f "$CONTAINER_NAME" >/dev/null
fi

docker run -d \
  --name "$CONTAINER_NAME" \
  --restart unless-stopped \
  --env-file "$RUNTIME_FILE" \
  -p 127.0.0.1:5272:8080 \
  "$IMAGE_NAME"

echo "Started $CONTAINER_NAME on 127.0.0.1:5272"
