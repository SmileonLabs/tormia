#!/usr/bin/env sh
set -eu

ROOT_DIR="${1:-/opt/tormia-test}"
SECRET_DIR="$ROOT_DIR/secrets"
UDP_FILE="$SECRET_DIR/udp-transport.env"
KEY_ID="${UDP_CREDENTIAL_PROTECTION_KEY_ID:-tormia-test-udp-v1}"

case "$KEY_ID" in
  *[!A-Za-z0-9._-]*|'')
    echo "UDP credential key ID must use only A-Z, a-z, 0-9, dot, underscore, or dash." >&2
    exit 1
    ;;
esac

mkdir -p "$SECRET_DIR"
chmod 700 "$SECRET_DIR"
umask 077

if [ ! -f "$UDP_FILE" ]; then
  key="$(openssl rand -base64 32 | tr -d '\r\n')"
  {
    printf '%s\n' 'Realtime__UdpEnabled=true'
    printf '%s\n' 'Realtime__UdpListenerEnabled=true'
    printf '%s\n' 'Realtime__UdpListenerPort=5273'
    printf '%s\n' 'Realtime__UdpRequireHttps=true'
    printf '%s\n' 'Realtime__UdpAllowSingleInstanceDevelopment=false'
    printf 'Realtime__UdpCredentialProtectionKeyBase64=%s\n' "$key"
    printf 'Realtime__UdpCredentialProtectionKeyId=%s\n' "$KEY_ID"
  } > "$UDP_FILE"
fi

chmod 600 "$UDP_FILE"

key="$(sed -n 's/^Realtime__UdpCredentialProtectionKeyBase64=//p' "$UDP_FILE")"
key_id="$(sed -n 's/^Realtime__UdpCredentialProtectionKeyId=//p' "$UDP_FILE")"
decoded_length="$(printf '%s' "$key" | base64 -d 2>/dev/null | wc -c | tr -d ' ')"

if [ "$decoded_length" != "32" ] || [ -z "$key_id" ]; then
  echo "Existing UDP credential file is invalid; refusing to rotate or overwrite it." >&2
  exit 1
fi

for required in \
  'Realtime__UdpEnabled=true' \
  'Realtime__UdpListenerEnabled=true' \
  'Realtime__UdpListenerPort=5273' \
  'Realtime__UdpRequireHttps=true' \
  'Realtime__UdpAllowSingleInstanceDevelopment=false'
do
  if ! grep -Fxq "$required" "$UDP_FILE"; then
    echo "UDP credential file is missing required fail-closed setting: $required" >&2
    exit 1
  fi
done

echo "Stable UDP runtime secret is ready at $UDP_FILE (key material not printed)."
