# Hybrid UDP transport selection

## Decision

TOV selects LiteNetLib 2.1.4 for the first UDP motion vertical slice. The
server consumes the exact NuGet version and Unity consumes the exact OpenUPM
package version. The OpenUPM artifact resolves to upstream revision
`3bfa89948f6fa4e00c421f75cbd999e048f493d8` with registry SHA-512 integrity
`lLx3T31CW665adCUOaD6jqxARbjumqeM/29fnZ5091bPrbghjRFJgC4kDQMumgy7Sl+4zbZjpWi7MPR886aVXw==`.

LiteNetLib is only the datagram delivery substrate. It does not own identity,
gameplay permission, movement rules, Authority state, projection recovery, or
durable data. HTTPS and SignalR remain the authenticated control and recovery
lanes.

## Deferred alternatives

- Unity Transport is deferred because the current Authority is an ASP.NET Core
  .NET 8 service rather than a Unity headless host.
- QUIC is deferred because `System.Net.Quic` is preview on .NET 8 and the Unity
  Android IL2CPP path would add an unverified native dependency.

## Go/no-go gates

The UDP lane remains disabled until all of these pass:

- the pinned package compiles in Unity Editor, Android IL2CPP, and Linux .NET 8;
- HTTPS issues a short-lived, one-use transport ticket without exposing a bearer
  token in a datagram;
- datagrams are authenticated, generation-bound, replay-window checked, and
  rejected before payload allocation when invalid;
- packet loss, reordering, reconnect, UDP-blocked fallback, and stale-session
  tests preserve the existing Authority motion contract;
- the external test deployment exposes an isolated UDP port without changing
  the existing HTTPS virtual host.

