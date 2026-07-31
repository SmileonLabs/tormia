# In-place jump presentation lifecycle

## Summary

- **Date:** 2026-07-31
- **Owner:** TOV gameplay/ontology
- **Status:** Implemented and verified
- **Request:** Remove the remaining second-bounce appearance after landing without adding another local jump patch.

## Intent

Keep the approved parabolic motion under the single CharacterController owner,
while making one accepted jump produce one traceable presentation lifecycle.

## Data classification

- Runtime observation: grounded state, vertical velocity, jump occurrence.
- Inferred presentation state: `JumpStart`, `Airborne`, `Fall`, `Landing`.
- Unity presentation data: manifest playback segment and root-motion mode.
- Durable gameplay permission remains unchanged in `jump_action` and
  `JumpPlayerFromIntent`.

## Decision and boundary

An accepted jump increments an ephemeral occurrence counter once. The animation
resolver consumes that occurrence through `JumpStart`, observes vertical
collision state for `Airborne` and `Fall`, and enters `Landing` once when
support returns. One-shot phases normally finish from the selected clip
segment, not a fixed timer. The collision-resolved apex is an earlier physical
boundary: a takeoff clip that outlasts ascent cannot remain selected during
descent.

CharacterController remains the only world-motion owner. Jump lifecycle entries
must explicitly disable root motion. The third-party source clips are not
modified: the project manifest selects a short `JumpStart` segment and the
monotonically settling 0.85-1.00 segment of `Jump_End`.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled | One approval produces one occurrence and one ordered presentation lifecycle. | `ApprovedJumpOccurrenceCompletesEachPresentationPhaseOnce`; `ApprovedJumpCreatesOneOccurrenceAndConsumesSupport` |
| Apex | Negative vertical velocity ends an overlong `JumpStart` and selects `Fall`. | `DescendingAvatarCannotRemainInJumpStart` |
| Content | Jump clips explicitly disable root motion and the landing segment has no upward root curve. | `PlayerJumpLifecycleUsesInPlaceSegmentsWithoutLandingRise` |
| Invalid/removed | Invalid segments or inherited jump root motion fail validation; a second approval after support consumption is rejected. | `JumpLifecycleManifestRequiresSegmentedInPlacePresentation`; PlayMode occurrence test |

## Performance and multiplayer impact

The occurrence is local ephemeral state. No per-frame Fact, Authority command,
database event, or additional transform solver is added.

## Updated documents

- `AGENTS.md`
- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
