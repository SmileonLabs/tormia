# Hybrid realtime transport baseline

## Decision

Instrument the existing HTTPS/SignalR Authority motion path before adding UDP.
UDP will be a replaceable high-frequency transport and will not own semantic
permission, durable state, combat results, lifecycle, or presence recovery.

## Confirmed baseline

- Unity submits movement intent at a nominal 15 Hz and collision-resolved pose
  observations and local Authority reads at 10 Hz.
- Authority advances player motion at a 20 Hz fixed target tick and publishes
  changed motion states through SignalR.
- The previous implementation had no operational evidence for reconnect reason,
  snapshot age, reorder, buffer underrun, tick overrun, or publish latency.

## Evidence added

- Server `RealtimeTransportMetrics` records fixed-tick and SignalR publication
  health with low-cardinality tags.
- Unity `OntologyRemoteMotionRuntimeMetrics` exposes session-local transport and
  interpolation evidence without writing Facts or changing rules.
- UDP remains disabled until authentication, loss, reorder, failover, Android/PC,
  and external-network gates pass.
