# Normalized animation transition

## Summary

- **Date:** 2026-07-29
- **Owner:** Codex with project owner
- **State:** Implemented and verified
- **Scope:** Unity ontology animation presentation

## Intent

Remove the visible first-step avatar sink that occurred while changing from an
idle ontology animation clip to locomotion.

## Data classification

- Unity presentation
- Ephemeral animation state

No account profile, durable world Fact, inference, Rule Block, action
permission, or physical profile is added or changed.

## Root cause and decision

The player root Transform and `CharacterController` stayed on the support
surface during reproduction. The hips and feet dropped only during the
idle-to-walk blend.

The adapter placed the outgoing and incoming full-body clips on separate
`AnimationLayerMixerPlayable` override layers and assigned complementary
weights. Override layers compose sequentially; their weights do not form a
normalized clip cross-fade. At the middle of the transition this allowed the
controller or bind pose to contribute and lowered the avatar.

The adapter now keeps one fully weighted ontology presentation layer. A
normalized two-input `AnimationMixerPlayable` performs clip-to-clip
cross-fades inside that layer. Controller-to-clip and clip-to-controller
weights remain at the outer layer boundary.

## Ontology boundary

Triples, assigned Rule Blocks, Authority action results, canonical animation
intents, and actor repertoire still decide which behavior and clip are
available. The mixer only presents an already selected clip. It provides no
fallback when the ontology contract is absent.

## Verification

| Case | Expected | Evidence |
| --- | --- | --- |
| Active | Idle-to-walk uses normalized clip weights while the full-body layer remains fully weighted | `ClipToClipBlendKeepsFullBodyLayerFullyWeighted` |
| Disabled | Missing intent or repertoire still removes selected playback | Existing ontology animation selection tests |
| Runtime | Player root and controller stay fixed while the transition no longer admits the bind/controller pose | Reproduction probe plus PlayMode verification |

Full verification passed: EditMode 175/175, PlayMode 47/47, Unity Console
0 new errors, and the development harness including Unity MCP, Docker services,
and World Authority health.
