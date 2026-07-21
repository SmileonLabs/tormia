# Tormia Project Context

> **Language**: Korean companion: [`PROJECT_CONTEXT.ko.md`](PROJECT_CONTEXT.ko.md).
> Keep both files updated in the same change.
>
> **Purpose**: This is the shared decision context for continuing Tormia development.
> Update this document when a system boundary, ontology rule, data owner, or
> player-facing design decision changes. It is not a replacement for code or
> API documentation; it records the reasons and rules that keep them coherent.

## 1. Product direction

Tormia is a sandbox world-building game. A player enters a prepared world,
places objects, defines their ontology data and rule blocks, then sees the
world react through simulation, animation, physics presentation, interaction,
and later multiplayer.

The central idea is **data-defined meaning**:

- an object is not hard-coded as a tree, tube, boat, or rock;
- its meaning comes from its ontology triples, selected physical profile, and
  attached rule blocks;
- Unity is responsible for seeing, touching, animating, and presenting the
  result; it must not silently become a second rule engine.

The immediate content concept is: cross the sea and build a personal world by
placing objects and defining their relationships and behaviors.

## 2. Current working baseline

| Area | Current state |
| --- | --- |
| Unity | Unity 6 (`6000.4.7f1`), main scene: `Assets/Scenes/TormiaMain.unity` |
| Visual style | Poly-style environment assets; runtime UI uses the Farm Game UI kit as its art basis |
| World authoring | Runtime world edit, object placement, ontology triple editing, rule-block and physics selection |
| Ontology runtime | Facts, rule database, inference, actions, quests, animation intent selection, localization |
| Environment and equipment examples | Water-region detection, movement presentation, physical profiles, and wearable/equippable world objects |
| Local services | Docker PostgreSQL + Redis + .NET World Authority at `http://127.0.0.1:5272` |
| Multiplayer foundation | World revision commands, zones, transient input, kinematic authority motion, SignalR notifications, remote-avatar presentation |
| Account foundation | Development identity, account-owned character profiles, saved character/world selection, Unity entry flow |

The platform is a **development foundation**, not a released production service.

## 3. System map and ownership

```text
Player / World Editor
        |
        v
Unity client -- intended command --> World Authority API
   |                                         |
   |                               PostgreSQL: durable world state
   |                               Redis: transient coordination / runtime leases
   |
   +-- local adapters: renderer, animator, CharacterController,
       UI, water/physics presentation
```

### Ownership rules

| Owner | Owns | Must not own |
| --- | --- | --- |
| Account service | User identity, saved character profile, character template, chosen part IDs | Shared world meaning or inferred simulation state |
| World Authority | Durable placed entities, authored triples, rule-block bindings, permissions, revisions/events | Passwords or Unity scene internals |
| Ontology engine | Applying data-defined rules and producing inferred state | Rendering, direct transform movement, UI layout |
| Unity adapters | Input capture, collision/terrain contact, animation, renderer state, visual physics | A hidden duplicate of a gameplay rule |
| PostgreSQL | Durable, revisioned data | Per-frame observations or input samples |
| Redis | Expiring intent, motion, session and coordination state | Permanent world history |

Unity never accesses PostgreSQL or Redis directly. It talks only to the World
Authority HTTP/realtime boundary.

## 4. Ontology policy (non-negotiable)

### 4.1 Fact categories

| Category | Example | Where it belongs | Persistence |
| --- | --- | --- | --- |
| Authored/static Fact | `BeachWater_01 has_concept WaterRegion` | World authoring data | Durable world state |
| Observed/runtime Fact | `Player immersion_depth Deep` | Runtime observation produced by an adapter | Ephemeral; recomputed |
| Inferred Fact | `Player movement_mode Swimming` | Rule-engine output | Derived; do not author as a substitute for its cause |
| Account profile data | Character name, template, equipped visual-part IDs, canonical profile relations (skills, owned pets, carried items) | Account character profile | Durable account data, **not** a world Fact |
| Presentation state | Animator blend, renderer enabled, bobbing position | Unity adapter | Ephemeral local presentation |

`BeachWater_01 water_depth Deep` describes the region. It is not the same as
the player currently being deeply submerged. The first is authored world data;
the second is an observed runtime condition.

### 4.2 Triple editing

The runtime triple editor follows:

```text
Subject (fixed selected object) --> Relation --> Object
```

- **Subject** is the selected world entity and is not edited inside a row.
- **Relation** and **Object** are canonical ontology terms, shown in the
  selected language but stored in canonical English IDs.
- A concept such as `Plant`, `Rock`, or `WaterRegion` is data, not a hard-coded
  promise about the mesh. A tree mesh may be intentionally declared a rock.
- The UI warns about invalid terms or incompatible known definitions; it does
  not prohibit creative authoring merely because the visual mesh looks unusual.

Canonical IDs, localized labels, aliases, and migrations are maintained in
`Assets/Resources/Ontology/Localization`. Read its README before changing terms.

### 4.3 Rules and rule blocks

A rule says what becomes true when ontology conditions are true. A rule block
is the authorable, placeable/bindable world object that enables a rule for the
relevant entity or region.

- Do not solve a new gameplay relationship by adding a mesh-name `if` statement
  in Unity.
- Put reusable meaning in a rule definition or rule-block template.
- Put object-specific choices in triples, selected rule blocks, or physical
  profiles.
- Unity adapters may map an inferred state such as `movement_mode Swimming` to
  animation and movement presentation, but they do not decide *why* swimming is
  allowed.
- Removing a rule block removes that world behavior. It must not continue due
  to a fallback hard-coded behavior.

### 4.4 Physical behavior, environment, and equipment

Physical profiles are ontology-selected behavior presets. They translate a
player-friendly choice such as “float lightly on water” into data-driven
parameters for a Unity physics/presentation adapter. Water, swimming, and the
tube are the first working examples of this general pattern; they are not a
closed list of special cases.

- A physical profile is a single primary behavior; optional secondary effects
  (wind push, wave sway) are additive relations rather than conflicting primary
  profiles.
- Environment placement and response are decided from the selected object’s
  data/profile, not its filename. Water is the first environment implementation.
- World equipment (tube, boat, mount, tool, wearable object, and future types)
  has independent properties: collectable, wearable, slot/attachment behavior,
  physical behavior, and granted temporary skill.
- An equipped world object may grant a temporary ability required by a rule. A
  tube granting swimming is the first example. This does not turn the world
  object itself into the player’s permanent account profile.

## 5. Character and account lifecycle

```text
Authenticated identity
  --> account-owned character profile
  --> selected character + selected world
  --> registered world avatar
  --> world entry
  --> local appearance adapter applies saved profile
```

Current local development flow uses the automatic development subject
`local-editor-1`; it deliberately has no visible login screen. It is a test
convenience, not a production credential system.

The account character profile contains name, template, selected visual-part
IDs, and canonical profile relations such as skills, owned pets, and carried
items. On world entry, Unity projects the visual-part portion of that profile into the local character part
adapter. It must not be copied into arbitrary world facts just to remember an
account choice. Shared world avatar appearance synchronization is a separate
multiplayer/presentation concern.

Production login later replaces the development header with verified
JWT/OIDC/session identity while preserving this same account --> character --> world
boundary.

## 6. Authoring and UI principles

- Runtime world editing should be understandable without Unity Inspector access.
- The selected-object toolbar is for transform actions and opening ontology
  editing; ontology editing itself appears only when the **Ontology** action is
  chosen.
- The `Tab` interaction selects nearby objects and cycles among nearby targets;
  it must not move the player or leak clicks through UI.
- Game UI assets and layout belong to project-owned UI hierarchy/assets. Do not
  recreate the same UI simultaneously through an unrelated generated script.
- `WorldEditHUD` and the selected-object edit handle are editable prefabs under
  `Assets/Prefabs/Ontology/UI`. Repeating triple, rule, physics, and result
  rows are authored as child templates of `WorldEditHUD`; runtime cloning is
  allowed only to represent the number of authored data rows.
- Runtime UI binders may set text, options, state, and callbacks. They must not
  create visual controls, choose layout values, or duplicate a prefab hierarchy.
- Preserve user-made hierarchy/UI adjustments unless the requested change
  explicitly replaces them.
- Third-party demo assets are references. Project changes live in
  `TormiaMain.unity`, project-owned prefabs, and data assets.

## 7. Localization contract

- Player-visible text is localized through CSV language packs.
- Canonical ontology IDs remain English and are stored exactly as defined.
- Input can be Korean or English through aliases; the resolved canonical ID is
  what gets saved.
- Relation IDs are `snake_case`; concept/state/value IDs are normally
  `PascalCase`.
- Do not localize free instance names automatically.
- Rename saved IDs through `OntologyMigrations.csv`, never by changing an ID in
  place.

## 8. Networking and performance rules

- Durable edits are commands with world revision and idempotent command IDs.
- Transient inputs, observed state, motion samples, and live presence must not
  create a durable database event every frame.
- Zone projections limit what each client loads; cross-zone references remain
  identifiers until the other zone is relevant.
- The current server motion model is zone-bounded kinematic motion. Unity
  terrain collision, gravity, water response, mounting, and animation remain
  local presentation until equivalent server simulation exists.
- New per-object behavior must be evaluated for polling frequency, allocations,
  network fan-out, rule iteration cost, and database write rate.
- The selected world's authority role is part of the account/world session
  contract. Unity may expose authoring controls only for `owner` or `editor`;
  the World Authority API remains the final permission check and rejects viewer
  commands.

## 9. Established decisions

| Decision | Why |
| --- | --- |
| Environment volumes and land collision are separate authoring concerns | A water volume is the first example: it can be observed without pretending the environment is solid ground |
| Movement modes use observed actor state, not a region fact alone | Swimming is the first example: a deep-water region does not mean every actor is currently swimming |
| Animation database and character profile are separate | Database defines available clips; profile selects what the actor may use |
| Capabilities are not animation metadata gates | Character/world relationships determine permission; animation maps resolved intent to a clip |
| Rule Blocks are data-bound world behavior | Removing a block must remove its effect without code changes |
| Character profile is account data | The same profile can enter many worlds without changing world ontology |
| Local development identity auto-starts | Enables testing while real authentication is still pending |
| UI is data/asset/hierarchy-led | Prevents duplicate script-built and hierarchy-built UI from conflicting |

## 10. Working agreement for future changes

Before implementing a request, answer these questions:

1. Is this **account profile**, **durable world data**, **runtime observation**,
   **inference**, or **Unity presentation**?
2. Can a data/rule/profile change express it without an object-name or mesh-name
   condition?
3. If it is a Unity adapter, what ontology fact or resolved intent authorizes it?
4. Does it need a canonical term, localization entry, alias, or migration?
5. Is it a durable revisioned command, or an ephemeral runtime signal?
6. Does it respect multiplayer authority and zone scope?
7. What test demonstrates both the enabled and removed/disabled behavior?

When the answer is unclear, first add the missing ontology data shape or rule
template. Do not patch visible behavior with a one-off fallback.

## 11. Next work order

1. Keep this context current whenever a system-level decision changes.
2. Design the visible account/character selection UI, then replace the automatic
   local development entry only when verified authentication is available.
3. Continue content development: world object catalog, reusable rule-block
   templates, quests/NPCs, and player-facing creation flows.
4. For production: verified identity provider, server-side profile appearance
   projection for remote avatars, deployment/observability/security hardening,
   and load testing.

## 12. Development harness

The repository uses a lightweight harness to make human/AI-assisted changes
repeatable and reviewable. Its core assets are `AGENTS.md`, the regression
scenario manifest, verification scripts, and bilingual change records.

- Run `scripts/verify-development.ps1` before considering a system change
  complete. It validates the context pair, scenario evidence, and the authority
  build (using Docker when a local .NET SDK is unavailable).
- Run `scripts/run-unity-harness-tests.ps1` for the relevant Unity mode.
- Treat a failing scenario as information, not a reason to add a fallback. Fix
  the data, rule, adapter, or test isolation at the responsible layer.
- Record system-level changes in `docs/changes` in English and Korean.

## Related documents

- [Korean project context](PROJECT_CONTEXT.ko.md)
- [Project README](../README.md)
- [Local platform services](../infrastructure/README.md)
- [Ontology language-pack contract](../Assets/Resources/Ontology/Localization/README.md)
- [Actor animation handoff](../ACTOR_ANIMATION_HANDOFF.md)
