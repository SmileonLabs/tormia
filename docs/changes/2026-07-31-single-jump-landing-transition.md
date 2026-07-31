# Single Jump and Landing Transition

## Summary

- **Date:** 2026-07-31
- **Owner:** TOV team
- **Status:** Implemented
- **Related issue or request:** The player appeared to jump a second time on landing.

## Intent

Ensure one physical jump and one animation occurrence are produced from one
Authority-approved input edge, and prevent the airborne pose from surviving
after collision grounding.

## Data classification

- [ ] Account profile
- [ ] Durable authored world data
- [x] Runtime observation
- [x] Inferred state
- [x] Unity presentation
- [x] Transport / authority / infrastructure

## Decision and boundary

Raw jump input is a coalescing, single-consumer ephemeral edge. The intent
sender consumes it once when transport is ready. The collision and animation
adapters receive only the later Authority-approved presentation edge.

Animation transition ownership remains in the manifest. `canBlend=true`
cross-fades through the normalized mixer. `canBlend=false` cuts immediately;
the adapter no longer ignores this authored value. The current Landing entry
is already non-blendable, so its pose replaces Airborne as soon as the
CharacterController reports contact. No clip name or character name exception
was added.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Input enabled | Repeated observation of one pending edge is consumed once and produces at most one submission. | `EphemeralJumpIntentCanBeConsumedOnlyOnce` |
| Landing enabled | A non-blendable Landing entry immediately removes the Airborne mixer weight. | `NonBlendableLandingCutsAirbornePoseImmediately` |
| Rule removed | Missing `jump_action` or its assigned Rule Block consumes no hidden Unity jump behavior. | `RemovingJumpActionFactRemovesUnityIntentRoute`; `RemovingPlayerJumpRuleBlockRemovesJumpBehavior` |

## Documentation updated

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] Test scenario manifest
