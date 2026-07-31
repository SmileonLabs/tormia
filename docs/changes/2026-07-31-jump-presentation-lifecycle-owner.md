# Jump presentation lifecycle owner

## Decision

World Authority remains the source of jump permission and the accepted
canonical animation intent. For a local avatar using manifest-driven direct
character motion, the collision-backed animation state resolver exclusively
owns the runtime `JumpStart` → `Airborne`/`Fall` → `Landing` lifecycle.

## Reason

The generic Authority transient presenter treated `JumpStart` as a
non-interruptible one-shot clip. That prevented the physical-state resolver
from changing presentation at the observed apex and landing even though
CharacterController collision and grounding were correct.

The content manifest also assigned canonical airborne `Fall` to a ground
collapse reaction because the source clip shared the same word. The direct
motion `Fall` intent now continues the in-place airborne clip; the collapse
clip retains only its reaction intent.

## Evidence

- `AuthorityJumpPresentationUsesPhysicalStateResolverOwnership`
- `DescendingAvatarCannotRemainInJumpStart`
- Removing the jump Rule Block still prevents the jump occurrence; attacks and
  equipment actions continue to use generic Authority transient presentation.
