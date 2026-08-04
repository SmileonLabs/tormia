# Invited world entry avatar identity

## Decision

World entry uses the content package explicitly assigned to the selected world.
When a member has no avatar in that world, Unity requests Authority provisioning
with an avatar identity derived from the project avatar seed, world ID, and
authenticated user ID. Existing registered avatar IDs remain unchanged.

## Reason

Using a visitor-scoped content package rejected valid invited worlds. Deriving a
new avatar from only the scene seed and world ID also made every user collide
with the owner's avatar identity, causing `avatar_owned_by_another_user`.

## Verification

- The deterministic identity is stable for the same world and user.
- Different users in the same world receive different avatar identities.
- An invited test account completed entry into the shared remote test world.
- The server remains the authority that validates and provisions the avatar.
- Read-only semantic-contract admission preflight retries the same immutable
  manifest on transient response loss; it never prepares, changes, or bypasses
  a rejected contract.
