# Authority-projected player hit reaction

- Date: 2026-08-01
- Status: implemented and asset-pipeline validated

Player hit presentation now consumes a newer Authority projection that lowers
`current_health`. PlayerProfile authors the `HitReaction` intent and the
project-owned Mixamo FBX is registered through the validated Manifest,
Database, and Profile production line with root motion disabled.

Removing the hit-intent Triple or Manifest/Profile membership removes the
presentation route. Unity collision and object names do not create damage or
grant the reaction.

Existing world avatars migrate through player semantic contract version 10.
The migration authors only the missing `hit_animation_intent=HitReaction`
Triple and then advances the version marker. It does not restore unrelated
player semantics, and a later user removal remains effective.

The realtime revision hint now identifies the committed damage occurrence,
its target, and whether Rule evaluation produced damage. The matching presenter
consumes the event ID once at contact time. Projection health comparison remains
only a transport-fallback path and cannot replay an already consumed event.
