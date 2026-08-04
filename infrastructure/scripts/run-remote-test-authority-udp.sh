#!/usr/bin/env sh
set -eu

ROOT_DIR="${1:-/opt/tormia-test}"
IMAGE_NAME="${2:-}"
CONTAINER_NAME="${TORMIA_TEST_CONTAINER_NAME:-tormia-test-authority}"
CANDIDATE_NAME="$CONTAINER_NAME-udp-candidate"
BACKUP_NAME="$CONTAINER_NAME-rollback-stage13"
NETWORK_NAME="${TORMIA_TEST_DOCKER_NETWORK:-bridge}"
RUNTIME_FILE="$ROOT_DIR/secrets/world-authority.env"
UDP_FILE="$ROOT_DIR/secrets/udp-transport.env"
HTTP_PORT="${TORMIA_TEST_HTTP_PORT:-5272}"
UDP_HOST_PORT="${TORMIA_TEST_UDP_PORT:-5273}"
CANDIDATE_PORT="${TORMIA_TEST_CANDIDATE_PORT:-5274}"

if [ -z "$IMAGE_NAME" ]; then
  echo "Usage: $0 [root-dir] <immutable-image-tag>" >&2
  exit 1
fi
for required in "$RUNTIME_FILE" "$UDP_FILE"; do
  if [ ! -f "$required" ]; then
    echo "Missing runtime file: $required" >&2
    exit 1
  fi
done
docker image inspect "$IMAGE_NAME" >/dev/null

proxy_address="$(docker network inspect "$NETWORK_NAME" \
  --format '{{(index .IPAM.Config 0).Gateway}}')"
case "$proxy_address" in
  ''|*[!0-9a-fA-F:.]*)
    echo "Could not resolve the exact Docker bridge proxy address." >&2
    exit 1
    ;;
esac

wait_healthy() {
  container="$1"
  attempts=0
  while [ "$attempts" -lt 90 ]; do
    state="$(docker inspect "$container" \
      --format '{{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{end}}')"
    case "$state" in
      running\|healthy) return 0 ;;
      exited\|*|dead\|*) return 1 ;;
    esac
    attempts=$((attempts + 1))
    sleep 1
  done
  return 1
}

verify_realtime() {
  port="$1"
  body="$(curl --fail --silent --show-error \
    "http://127.0.0.1:$port/health/realtime")"
  printf '%s' "$body" | grep -q '"status":"healthy"'
  printf '%s' "$body" | grep -q '"backplane":"redis"'
  printf '%s' "$body" | grep -q '"udpTicketEnabled":true'
  printf '%s' "$body" | grep -q '"udpListenerEnabled":true'
  printf '%s' "$body" | grep -q '"udpTicketBackend":"redis_multi_instance"'
}

if docker ps -a --format '{{.Names}}' | grep -Fxq "$CANDIDATE_NAME"; then
  echo "Candidate container already exists: $CANDIDATE_NAME" >&2
  exit 1
fi
if docker ps -a --format '{{.Names}}' | grep -Fxq "$BACKUP_NAME"; then
  echo "Rollback container already exists: $BACKUP_NAME" >&2
  exit 1
fi
if ! docker ps -a --format '{{.Names}}' | grep -Fxq "$CONTAINER_NAME"; then
  echo "Current isolated Authority container is missing: $CONTAINER_NAME" >&2
  exit 1
fi

docker run -d \
  --name "$CANDIDATE_NAME" \
  --restart no \
  --network "$NETWORK_NAME" \
  --env-file "$RUNTIME_FILE" \
  --env-file "$UDP_FILE" \
  -e "Realtime__TrustedProxyAddresses=$proxy_address" \
  -p "127.0.0.1:$CANDIDATE_PORT:8080/tcp" \
  "$IMAGE_NAME" >/dev/null

if ! wait_healthy "$CANDIDATE_NAME" || ! verify_realtime "$CANDIDATE_PORT"; then
  docker logs "$CANDIDATE_NAME" --tail 100 >&2 || true
  docker rm -f "$CANDIDATE_NAME" >/dev/null || true
  echo "Candidate validation failed; current service was not changed." >&2
  exit 1
fi
docker rm -f "$CANDIDATE_NAME" >/dev/null

rollback() {
  echo "New Authority failed health gates; restoring retained container." >&2
  docker rm -f "$CONTAINER_NAME" >/dev/null 2>&1 || true
  if docker ps -a --format '{{.Names}}' | grep -Fxq "$BACKUP_NAME"; then
    docker rename "$BACKUP_NAME" "$CONTAINER_NAME"
    docker start "$CONTAINER_NAME" >/dev/null
  fi
}

docker stop "$CONTAINER_NAME" >/dev/null
docker rename "$CONTAINER_NAME" "$BACKUP_NAME"

if ! docker run -d \
  --name "$CONTAINER_NAME" \
  --restart unless-stopped \
  --network "$NETWORK_NAME" \
  --env-file "$RUNTIME_FILE" \
  --env-file "$UDP_FILE" \
  -e "Realtime__TrustedProxyAddresses=$proxy_address" \
  -p "127.0.0.1:$HTTP_PORT:8080/tcp" \
  -p "$UDP_HOST_PORT:5273/udp" \
  "$IMAGE_NAME" >/dev/null; then
  rollback
  exit 1
fi

if ! wait_healthy "$CONTAINER_NAME" || ! verify_realtime "$HTTP_PORT"; then
  docker logs "$CONTAINER_NAME" --tail 100 >&2 || true
  rollback
  exit 1
fi

echo "UDP candidate promoted. Retained rollback container: $BACKUP_NAME"
echo "Exact trusted proxy: $proxy_address; UDP mapping: $UDP_HOST_PORT/udp"
