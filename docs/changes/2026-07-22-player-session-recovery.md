# Player session and account UI recovery

## Summary

- **Decision:** Resume the interrupted player/account UI slice from a recovery
  checkpoint and enable the already-authored multiplayer presentation path.
- **Status:** Implemented and verified.
- **Scope:** Unity presentation, runtime intent transport, account session data.

## Ownership

Account character appearance and profile relations remain account-owned.
Runtime movement samples and presence remain ephemeral Authority messages.
The sample `Ontology_Player` continues to contribute authored Creature, item,
and skill facts to the canonical `Player` entity, but no longer owns a second
animation adapter or actor-profile synchronizer.

## Scene configuration

- Runtime player intent submission is enabled after account/world entry.
- Remote avatar replication is enabled and bound to the realtime client, a
  dedicated `RemoteAvatars` root, the avatar prefab, animator source, and
  character-part database.
- The account entry flow now serializes its Authority client, intent sender,
  local avatar identity, and character-part adapter references.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| intent/replication enabled | Entered local avatar may publish ephemeral motion and render remote projections | `TormiaMain` serialized flags and bindings |
| feature not entered/connected | No durable per-frame world facts are created | Sender/Presenter readiness gates |
| account entry | Avatar is registered and saved appearance is applied | Live Play Mode status: character entered selected world |
| duplicate Player contributor | Authored sample facts remain, duplicate runtime adapters are absent | Live scene component audit |
| regressions | Core/UI/scene behavior remains stable | EditMode 52/52, PlayMode 32/32 |

Two independently running Unity clients are still required to visually inspect
cross-client interpolation; this run verified the local Authority registration,
entry, transport configuration, and server health.
