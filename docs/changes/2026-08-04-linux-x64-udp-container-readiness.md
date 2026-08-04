# Linux x64 UDP container readiness

## Decision

The World Authority production image is now explicitly published for
`linux-x64`, runs as the base image's non-root application user, exposes TCP
8080 and UDP 5273 as image metadata, and has a local TCP `/health` probe.
Exposing UDP in image metadata does not publish the port and does not enable
the listener. `Realtime:UdpEnabled` and `Realtime:UdpListenerEnabled` remain
false by default.

Enabled UDP is fail-closed outside explicit single-instance Development. It
requires Redis, a valid 32-byte credential protection key and key ID, and a
non-privileged listener port. Dependency-injection construction is validated
in the Linux Production container, not only in the Windows development host.

## Evidence

- A clean multi-stage Docker build restores and publishes the Authority for
  `linux-x64` with `UseAppHost=false`.
- The resulting `linux/amd64` image runs as uid/gid 1654 and resolves the .NET
  runtime native dependencies supplied by the ASP.NET 8 base image.
- A default-off Production container becomes healthy, reports both UDP flags
  false, and has no host UDP mapping.
- A local validation-only Production container with Redis and credential
  protection becomes healthy with both UDP flags true while still having no
  host UDP mapping.
- Missing Production UDP protection dependencies stop startup before the
  service can accept traffic.

## Stage 14 boundary

No Compose, cloud, firewall, load balancer, DNS, or AWS deployment was changed.
Stage 14 must add the real host UDP mapping deliberately and configure the
exact public proxy address in `Realtime:TrustedProxyAddresses`; broad proxy
trust is forbidden. HTTPS and SignalR remain the reliable control and recovery
lanes throughout rollout.
