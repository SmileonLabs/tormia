# Isolated remote UDP rollout preparation

## Summary

- **Date:** 2026-08-04
- **Owner:** TOV team
- **Status:** Prepared; remote upload and cutover require explicit approval
- **Scope:** `tormia-test-authority` and `/opt/tormia-test` only

## Confirmed topology

The current AWS test Authority runs as one Docker container with TCP 8080
published only at `127.0.0.1:5272`. Nginx on the same host terminates HTTPS for
`tov-api.punkarena.app` and forwards to that loopback port. The container uses
the default Docker bridge; its observed gateway is `172.17.0.1`. UDP 5273 is
not currently mapped or listening. UFW is inactive. Public `/health`,
`/health/realtime`, and `/health/scheduler` all returned HTTP 200 before any
cutover.

## Prepared rollout

- The immutable candidate image is
  `tormia-world-authority:test-20260804-udp-stage14`; its exported archive
  SHA-256 is
  `1ed9a6f9c5d4c070c90b80d51a4bc82c284d030e248393b11af774b94d01a86d`.
- `configure-remote-test-udp-runtime.sh` creates one stable 32-byte credential
  protection key with mode 0600 and never prints or rotates valid key material.
- `run-remote-test-authority-udp.sh` derives the exact Docker bridge gateway,
  validates an unmapped candidate on loopback TCP 5274, and promotes only after
  `/health/realtime` proves Redis, ticket, and listener readiness.
- The old container is retained as
  `tormia-test-authority-rollback-stage13`. Failed promotion restores it
  automatically. A separate rollback helper retains the failed UDP container
  for diagnosis.
- Final promotion maps only `127.0.0.1:5272:8080/tcp` and
  `5273:5273/udp`. HTTPS and SignalR remain the reliable control and recovery
  paths.

## Security boundary and blocker

The repository server note says the EC2 security group currently allows all
traffic, but AWS credentials were unavailable for an authoritative rule audit
or narrowing. Deployment must therefore use an external authenticated UDP
client probe as the reachability gate and must not claim a narrowed security
group. Remote artifact upload and cutover have not been performed because the
external transfer requires explicit user approval.
