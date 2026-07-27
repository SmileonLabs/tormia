# TOV runtime status and actor notification UI

## Scope

- Replaced the legacy technical runtime text block with an editable four-row
  presentation card for movement, interaction target, world permission, and
  save/sync state.
- Rebuilt the actor fact toast as separate card, accent, icon, title, detail,
  and pointer hierarchy objects.
- Added eight project-owned icon PNGs under
  `Assets/Art/UI/RuntimeStatusIcons`.

## Ownership and authority

This is a Unity presentation change. The HUD reads existing runtime input,
ontology facts, and World Authority client state. It does not create durable
facts, infer gameplay state, or bypass the World Authority API.

## Enabled and removed cases

- Enabled: the active game canvas presents the four compact status values, and
  actor fact changes can present a severity icon with title and optional detail.
- Removed: the HUD no longer presents the large legacy diagnostic fact dump.
  Existing one-string actor toast calls remain compatible and are split at the
  first colon when possible.

## Authoring and verification

Use `Tormia/UI/Apply TOV Runtime Status & Toast Design` to repair or regenerate
the editable hierarchy. The Tormia main-scene smoke test verifies the authored
status rows and toast layers.
