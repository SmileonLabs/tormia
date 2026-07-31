# TOV Project Context

> **Language**: Korean companion: [`PROJECT_CONTEXT.ko.md`](PROJECT_CONTEXT.ko.md).
> Keep both files updated in the same change.
>
> **Purpose**: This is the shared decision context for continuing TOV development.
> Update this document when a system boundary, ontology rule, data owner, or
> player-facing design decision changes. It is not a replacement for code or
> API documentation; it records the reasons and rules that keep them coherent.

## 1. Product direction

TOV is a sandbox world-building game. A player enters a prepared world,
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
| Unity | Unity 6 (`6000.4.7f1`), entry scene: `Assets/Scenes/TormiaBootstrap.unity`; authored scenes: `TormiaWorld.unity` and `TormiaUI.unity` |
| Visual style | Poly-style environment assets; runtime UI uses the Farm Game UI kit as its art basis |
| World authoring | Runtime world edit, object placement, ontology triple editing, rule-block and physics selection |
| Ontology runtime | Facts, rule database, inference, actions, quests, animation intent selection, localization |
| Environment and equipment examples | Water-region detection, movement presentation, physical profiles, and wearable/equippable world objects |
| Local services | Docker PostgreSQL + Redis + .NET World Authority at `http://127.0.0.1:5272` |
| Multiplayer foundation | World revision commands, zones, transient input, kinematic authority motion, SignalR notifications, remote-avatar presentation |
| Account foundation | Email/password authentication, opaque Bearer sessions, account-owned character profiles, saved character/world selection, Unity entry flow |

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
| World-avatar profile data | World-specific role, team, progression, class | Registered avatar overlay owned by World Authority | Durable, revisioned world data |

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
- A new reusable gameplay behavior is added to the rule database and published
  as a bindable Rule Block **before** input or transport integration is added.
  Compatible objects receive the behavior by authoring the required triples and
  assigning that block; a prefab, mesh, or original reference object is never
  the permanent owner of the logic.
- Input adapters and Authority transport commands may carry a canonical intent
  or observation. They must not directly write the final gameplay relation that
  the Rule Block is supposed to own. Checking `has_rule_block` as a guard while
  duplicating the result in an action effect does not satisfy this contract.
- The player-facing Rule Block page does not expose an incomplete
  bind-only authoring path. Players apply behavior through one data-defined
  Quick Setup entry backed by the Rule Block preset database; that entry
  submits the complete Triple + Rule Block + optional Physical Meaning package.
  The Rule Block list remains available for inspection and removal. Raw binding
  remains an internal production/publishing operation rather than a normal
  runtime authoring control.

The canonical gameplay production line is:

```text
trigger
  -> canonical intent or observation
  -> authored Triples + assigned Rule Block
  -> rule evaluation
  -> origin-classified result Fact/state/relation
  -> Authority persistence and projection when durable or shared
  -> Meaning/Profile
  -> Unity Adapter
  -> player experience
```

Authority persistence is conditional: ephemeral input, observation, and
inference must not become a database event merely to satisfy the diagram.
Physical Meaning is also a branch rather than a universal requirement. A
dialogue, quest, UI, audio, or animation result may use its matching
presentation meaning and adapter without a physics profile. What is never
optional is the evaluated, origin-classified result that authorizes the
adapter.

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

The local first slice now begins with scene-authored registration or login.
The Authority hashes passwords, issues opaque Bearer sessions, stores only a
hash of each session token, and validates that session for REST and SignalR.
Unity remembers the local session so it can restore the account dashboard, but
the current PlayerPrefs storage is a desktop development convenience and must
be replaced by platform secure storage before production release.

The Unity onboarding route uses dedicated login, registration, character
creation/selection, world creation/selection, profile review, and world-entry
loading panels. The former combined account-entry panel is not a fallback
route. When leaving character creation, an account with existing characters
returns to character selection; an account with no character signs out and
returns to login.

The account character profile contains name, template, selected visual-part
IDs, and canonical profile relations such as skills, owned pets, and carried
items. On world entry, Unity projects the visual-part portion of that profile into the local character part
adapter. It must not be copied into arbitrary world facts just to remember an
account choice. Shared world avatar appearance synchronization is a separate
multiplayer/presentation concern.

Character-part replacement is data-driven by canonical slots and
`conflicts_with_slot` facts. Only the canonical `Body` and `Face` definitions
are required; every wearable or visible optional part can be removed by
clicking its selected thumbnail again. Replacing or removing a covering part
does not remember or restore parts that it displaced, so removing a full-body
costume leaves the covered wearable slots empty. Variant renderers copy the
authored mesh, materials, and local bounds; a `useBaseRendererMesh` definition
restores the cached template renderer state.

Linked costume accessories are replaceable independently unless the parent
definition explicitly declares otherwise. Selecting a regular hat replaces the
linked costume hat in the shared `Headwear` slot while keeping the full-body
costume equipped; it does not restore displaced upper/lower clothing.

Required internal definitions may remain hidden from customization when they
offer no meaningful user choice. The base body remains a required
profile/renderer binding, while the optional placeholder upper-body mesh stays
hidden and does not create a one-item category.

The in-world character customization panel is a distinct runtime surface from
the account-creation appearance step. It may project part changes immediately
through the local adapter for preview and gameplay, but closing the runtime
panel persists the selected part IDs back to the currently selected
account-owned character profile through World Authority. The account-creation
panel never owns the runtime C-key or runtime open button.

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
- Third-party demo assets are references. Project changes live in the
  authoritative `TormiaWorld.unity` and `TormiaUI.unity` scenes,
  project-owned prefabs, and data assets.
- Runtime placement uses one editable **World Placement** shell with
  `Object`, `NPC`, and `Monster` modes. The mode is authored explicitly on
  each catalog definition; it is never inferred from a prefab, mesh, or object
  name. Categories and ontology summaries remain scrollable, hierarchy-owned
  presentation, while actual placement continues through the shared catalog
  and placement controller.

### 6.1 Actor reuse and portable/world-specific profile projection

- `OntologyActorProfile` is shared by players and NPCs. A profile supplies
  actor concepts, capabilities, and presentation availability; it does not
  create a special gameplay branch for a prefab or mesh.
- Account profile relations are portable character data. At world entry they
  are projected into the local avatar's runtime ontology view and are retracted
  on clear/disable; Unity must not publish that projection as a world Fact.
- A registered avatar may also have a **world-avatar profile overlay**. It owns
  world-specific role, team, class, and progression relations, is updated by a
  revisioned Authority command, and is never copied back to account data.
- NPC action selection uses the same actor-scoped candidate/action APIs as the
  player. The current Unity NPC controller is a development presentation and
  local-simulation adapter; multiplayer-authoritative NPC execution requires a
  published server action catalog before it is enabled for shared worlds.
- A world explicitly enables a content package version before it can use that
  package's published action definitions. The first authority action contract
  is intentionally narrow: the client supplies actor/target/tool entity IDs;
  the server resolves the immutable action definition and writes its one
  data-owned action Fact as a revisioned, idempotent command. Conditions and
  structured effects stay disabled until the headless evaluator owns them.
- The first-time development loop is deliberately one-click where possible:
  a connected account with no character can create a portable default character;
  first entry places and registers the stable scene avatar before binding that
  character. This is account/profile and Authority metadata, not a Unity-only
  fallback or an authored gameplay Fact.

## 7. Localization contract

- Player-visible text is localized through CSV language packs.
- Canonical ontology IDs remain English and are stored exactly as defined.
- Authority semantic identifiers use an ASCII English grammar. Localized
  Unicode labels are accepted only through the alias/localization boundary.
- Input can be Korean or English through aliases; the resolved canonical ID is
  what gets saved.
- Relation IDs are `snake_case`; concept/state/value IDs are normally
  `PascalCase`.
- Do not localize free instance names automatically.
- Rename saved IDs through `OntologyMigrations.csv`, never by changing an ID in
  place.
- Player-facing workflow and runtime feedback is carried as a localization key
  plus format arguments. Raw Authority, transport, and adapter status text is
  diagnostic data and must not be assigned directly to UI labels.
- Authored reusable content supplies both a canonical ID and language keys:
  placeables, physical profiles, rules, character templates, and shipped quests
  must pass project-coverage validation in English and Korean. User-authored
  names and quest prose remain pass-through content unless the author assigns
  an explicit localization key.
- Dynamic ontology summaries localize canonical concepts, relations, Rule Block
  names, profiles, and character parts only at the presentation boundary. The
  stored IDs and Authority commands remain canonical English.

## 8. Networking and performance rules

- Durable edits are commands with world revision and idempotent command IDs.
- Transient inputs, observed state, motion samples, and live presence must not
  create a durable database event every frame.
- Zone projections limit what each client loads; cross-zone references remain
  identifiers until the other zone is relevant.
- Player locomotion intent is Authority-validated and Zone-bounded, while
  Unity's `LocalCharacterController` Physical Meaning owns terrain/capsule
  collision. Unity publishes the newest collision-resolved pose as ephemeral
  runtime state for remote presentation. The server must not integrate a
  second collisionless player position or pull the local capsule toward it
  until equivalent shared collision geometry exists.
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
| Empty action/quest/rule catalogs stay empty | Removing data must not reactivate code-owned default behavior or synthesize an arbitrary verb Fact |
| Facts retain origin contributions | Durable, inferred, observation, account-profile, actor-profile, and appearance contributions may overlap without deleting each other |
| Only durable-origin facts enter local snapshots | Observations, inference, account profile projection, and character appearance are rebuilt by their owners |
| Authority actions use projection metadata | Unity resolves the enabled package, version, action, and definition version supplied by the server instead of hardcoding them in the action UI |
| Character profile is account data | The same profile can enter many worlds without changing world ontology |
| Account entry requires Authority authentication | Registration/login creates an opaque Bearer session; caller-provided user-ID headers and automatic development identities are not trusted |
| Runtime presentation starts only after confirmed world entry | A selected world is not an entered world; player rendering, HUD, input, streaming, and editing remain disabled until the Authority entry/restore pipeline completes |
| Resume uses coarse Authority checkpoints | Movement samples remain ephemeral; position/rotation are persisted periodically and on pause/logout through a revisioned idempotent command |
| Local recovery data is account/world scoped | Local snapshots and pending transport commands cannot leak between accounts or worlds and never contain a Bearer token |
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
8. If this introduces reusable gameplay logic, where is its registered,
   assignable Rule Block, and does input emit only intent rather than its final
   result?

When the answer is unclear, first add the missing ontology data shape or rule
template. Do not patch visible behavior with a one-off fallback.

## 11. Next work order

1. Keep this context current whenever a system-level decision changes.
2. Harden account authentication for production with rate limiting, recovery,
   email verification, session management, and platform secure token storage.
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
- Run `scripts/run-account-world-loop-smoke.ps1` against local services to
  verify registration, profile idempotency, entry gating, checkpoint autosave,
  logout, login, and resume. `verify-development.ps1 -RequireServices
  -RunAccountWorldLoopSmoke` includes this opt-in test.
- Treat a failing scenario as information, not a reason to add a fallback. Fix
  the data, rule, adapter, or test isolation at the responsible layer.
- Record system-level changes in `docs/changes` in English and Korean.

## 13. Actor reuse and Avatar profile layers

- `OntologyActorProfile` is the shared Actor definition for players and NPCs.
  Gameplay rules do not branch on prefab or mesh names.
- Account profile relations are portable character data. At world entry they
  project only into the local Avatar runtime ontology view and retract with it;
  they are not saved as world Facts.
- Profession, team, class, and world-specific progression belong to the
  revisioned World Authority `world-avatar profile` overlay.
- NPCs and players use the same actor-scoped action candidate/execution API.
  The current Unity NPC controller is a development/local simulation adapter;
  shared-world NPC execution remains disabled until server-owned evaluation is
  published.
- A world can execute only explicitly enabled, versioned content-package
  actions. The client sends actor/target/tool entity IDs and Authority resolves
  the immutable definition into a revisioned, idempotent action Fact.
- The first development loop may create a portable default character and
  place/register a stable scene Avatar before linking them. This is explicit
  account and Authority metadata, not a Unity-only gameplay fallback.

## 14. Session, autosave, and resume contract

```text
SignedOut -> Authenticated -> CharacterSelected -> WorldSelected
          -> EnteringWorld -> InWorld -> LeavingWorld / Recovering
```

- Account panels drive this lifecycle; Unity runtime objects do not infer login
  state from scene presence.
- Before `InWorld`, player renderers, world HUD/toasts, gameplay input,
  realtime/streaming adapters, quest UI, customization UI, and world editors
  are disabled. Failed entry returns to selection or recovery without exposing
  the world runtime.
- Entering a world loads the Authority projection, replays transport-failed
  idempotent commands, restores the avatar checkpoint, then applies account
  appearance/profile projections. Only completion enables the runtime.
- Avatar checkpoints are coarse durable state, saved at an interval after
  meaningful movement and on pause/logout. Per-frame input and motion remain
  ephemeral and never become database events.
- Appearance changes debounce into the account-owned character profile. Quest,
  action, inventory, and progression state is durable only when represented by
  an accepted account-profile or revisioned Authority command; UI-only state is
  not silently promoted to a world Fact.
- Local simulation snapshots are used only when no Authority world owns the
  session. Their path is scoped by account and world, written atomically, and
  excludes observations, inference, account projections, and appearance.
- The pending-command outbox stores only transport-failed durable commands,
  scoped by account/world. It retains the original command ID for idempotent
  replay and never stores credentials.

## 15. Creator workspace assistants

- Creator specialist NPCs live under a Unity `CreatorWorkspaceRoot` presentation
  layer. They are not authored world entities and do not contribute world Facts,
  observations, rule blocks, or authority commands merely by existing.
- The workspace is visible only after the owning account has entered the world
  and creator mode is enabled. Editor and viewer roles do not inherit this
  owner-only presentation.
- Temporary specialist appearances reuse the account character part database,
  but their selected part IDs are serialized presentation configuration. The
  appearance presenter never injects equipped-part Facts.
- Canonical service IDs identify the architect, resource, ontology, physics,
  rule, quest, UI, and QA/publishing roles. Localized display names remain
  presentation labels and are not persisted as ontology identifiers.
- Future conversational production services may replace each presentation NPC
  without migrating world data. Durable products created through a specialist
  must still cross the normal account-profile or revisioned Authority boundary.

## 16. Unity scene composition

- Player builds enter through `TormiaBootstrap`. It owns persistent Authority,
  session, save/restore, streaming, and ontology runtime services plus the one
  `EventSystem`.
- `TormiaWorld` is loaded additively and owns the camera, lighting, player,
  authored world objects, zones, physics presentation, and creator workspace.
- `TormiaUI` is loaded additively and owns account-flow canvases, in-world UI,
  preview studios, and editable panel hierarchy. UI may present or request
  behavior, but does not become the owner of world rules or durable Facts.
- `OntologySceneCompositionLoader` waits for both additive scenes, makes the
  world active, then refreshes scene-dependent bindings and rebuilds the local
  authored projection. Runtime gates still keep player and game UI hidden until
  the session reaches `InWorld`.
- `TormiaWorld` and `TormiaUI` are the only authoritative editable content
  scenes. Designers edit and save them directly. The former all-in-one
  `TormiaMain` snapshot is archived at
  `Assets/Scenes/Legacy/TormiaMain.unity`; it is not a source for regeneration,
  tests, or builds.
- `TormiaUI` owns exactly one project UI root, `OntologyGameCanvas`, plus its
  debug and preview roots. The empty third-party-era `FarmUI_DemoCanvas` and
  its legacy main-menu behavior are not part of the composition.
- Project setup validates the three separated scenes and build order. It must
  not regenerate or overwrite them from the archived integration snapshot.

## 17. Product brand and stable technical identifiers

- The player-facing product brand is `TOV`. Unity `productName` is `TOV`,
  `companyName` is `Smileon Labs`, and platform application identifiers use
  `com.smileonlabs.tov`.
- The repository folder, `Tormia.*` namespaces and assemblies, scene/script
  paths, Docker project/volume/network names, API headers, storage prefixes,
  database identifiers, and canonical ontology IDs remain unchanged.
- Existing World Authority accounts, characters, worlds, and checkpoints are
  not renamed or migrated because their durable IDs are independent of the
  Unity product label.
- Unity's company/product change creates a new local PlayerPrefs and
  `persistentDataPath` scope on some platforms. Credentials are never copied;
  a developer may need to sign in once again. Legacy local files are not
  deleted, while Authority-owned durable state resumes after authentication.

## 18. Stable entities and creator-request drafts

- Every `OntologyObject` has an explicit stable `entityId`. An editable Unity
  GameObject name is presentation metadata and is never used as an implicit
  ontology identity or rule-block binding.
- Newly placed world entities use the same GUID as their
  `OntologyAuthorityEntityIdentity`. Save format version 8 stores that ontology
  identity separately from the editable instance name; legacy records retain
  their prior instance name as a compatibility identity when no explicit ID
  exists.
- Movement contact, proximity, click intent, and simulation tick facts are
  runtime contributions. Their adapters retract only their own contribution,
  do not delete an authored or inferred equal-value contribution, and do not
  append per-frame movement events to durable session history.
- Player, creator-assistant, and remote-avatar renderers reuse one
  presentation-only character appearance projector. Equip/slot policy remains
  with the owning profile or adapter and is not hidden in that projector.
- Creator specialist definitions live in `CreatorServiceCatalog.asset`.
  Canonical English service IDs, stage order, workspace offsets, display
  localization keys, summaries, and temporary appearance part IDs are data,
  not an editor-script switch statement.
- Unity NPC decisions require an explicit `OntologyNpcDecisionPolicy`. The
  controller has no built-in `help`/`talk` preference and does not auto-run or
  execute locally unless a development/offline policy explicitly allows it.
  Shared-world NPC execution remains server-authoritative.
- The first World Architect slice captures natural-language input as an
  **ephemeral Draft** and previews the catalog production route. Creating that
  draft writes no world Fact and sends no Authority command. Durable resources,
  triples, physics, rules, quests, UI, and publication results require later
  reviewed steps through their normal owning boundary.

## 19. Refactoring boundaries

- Runtime UI activation uses explicit scene references and component contracts.
  It does not search for presentation GameObjects by name, so designers may
  rename and reposition the hierarchy without changing login/world-entry gates.
- Account selection previews, the local player, creator assistants, and remote
  avatars all use `OntologyCharacterAppearanceProjector`. Preview code may own
  cameras and framing, but it does not duplicate equip, linked-part, mesh, or
  material projection logic.
- `OntologyPlacedObjectFactProjection` is the only placed-record-to-world-Fact
  path. It selects the stable `entityId`, migrates legacy instance-name
  subjects, and keeps editable display names out of durable semantics.
- Creator routes are calculated by `OntologyCreatorRoutePlanner`. Domain errors
  are typed values; localized error messages and service summaries remain in
  the language pack rather than being returned by the workflow model.
- Local Authority session and selection preferences are isolated behind
  `OntologyAuthoritySessionStore`. They do not enter snapshots or command
  outboxes. A debounced appearance save captures the character and part IDs
  together so a later character selection cannot redirect the pending write.
- Avatar checkpoint retry reloads the projection only for
  `stale_revision`. Permission, validation, and transport failures do not run a
  hidden second command. Local snapshots and the Authority outbox share one
  atomic file replacement implementation while retaining separate ownership
  and backup policies.

## 20. First Authority combat production line

- Combat is a World Authority state transition, not a Unity collision rule.
  Action animation starts only after Authority accepts the command. Hit,
  health, death, and loot presentation additionally follows the refreshed
  projection.
- Published action definitions can contain ordered persistent effects:
  `SetFact`, `RemoveFact`, and bounded `AdjustNumberFact`. A later effect may
  use a generic numeric post-effect guard, such as `current_health <= 0`.
  Predicate names remain content data; the evaluator has no monster-, prefab-,
  or health-specific branch.
- The starter `attack` definition requires an explicitly equipped tool with
  the `Sword` concept and `grants_capability MeleeAttack`. It reduces
  `current_health` by the tool's numeric `attack_damage` Fact through the
  generic `valueFrom` effect source, with a minimum of zero. The target must
  also be `combat_disposition=Hostile`; `Damageable` alone does not authorize
  an attack. At zero the action sets `is_alive=False` and
  `loot_status=Available`. The target template owns `loot_table` and
  `loot_item`; the attack definition does not choose monster drops.
- The starter `equip_weapon` definition creates the item-owned
  `weapon equipped_by avatar` relation after checking the weapon's authored
  slot and Rule Block. It rejects a weapon already owned by another actor or a
  second item in the same slot and uses a generic
  `maxActorTargetDistance` runtime constraint evaluated from the Authority's
  ephemeral avatar motion state and the durable target transform. The local
  Unity distance check is targeting assistance only. `unequip_equipment`
  removes the selected relation through a revisioned Authority command and
  retracts a matching legacy `equipped_item` row when present. The client cannot
  supply a predicate, distance, or durable relation in an execution request.
- Attachment profiles explicitly declare their canonical relation predicate
  and whether that relation is `ItemToActor` or `ActorToItem`. The generic
  attachment adapter is the only component that parents an attached world
  item, resolves the stable `actorSocketId` and item-authored
  `OntologyAttachmentGripPoint`, and requests temporary physics overrides.
  An actor-owned `OntologyAttachmentSocket` follows an explicitly configured
  rig bone or prop anchor and owns the reusable palm pose. It is a project-owned
  sibling of imported character visuals, never an unsupported child override
  inside a third-party FBX instance. Shared profile offsets adjust that socket
  only; they are not a place for per-mesh weapon corrections. A prefab without
  a grip-point contract uses the generic profile pose for backward-compatible
  wearables and mounts. A placeable with the `Weapon` concept must provide an
  authored grip point and fails semantic validation when it does not.
  Combat presentation never contains a second direct-parenting fallback.
- Durable world transforms and attachment transforms have exclusive
  presentation ownership. A projection first applies semantic facts, resolves
  attachment ownership, and only then applies durable transforms to objects
  that remain world-owned. Removing the attachment relation returns transform
  ownership and the captured Rigidbody/Collider state to the world.
- `Sword01Bronze`, `Sword08Corrupted`, `Sword15FrostVisual`, and
  `BeholderBasic` are project-owned wrapper prefabs around imported assets.
  Weapon source prefabs, canonical families, capabilities, action IDs,
  animation intents, and VFX intents are entries in the editable
  `WeaponContentManifest` rather than fixed arrays in the catalog builder.
  Their ontology concepts, attachment profile, and initial health also live in
  project data assets. Weapon templates own `damage_profile` and
  `attack_damage`; monster templates own vitality, faction, disposition, and
  loot relations. The Unity presentation catalog no longer duplicates maximum
  health or drop identity. The word `Frost` in a source asset name does not
  grant a cold rule.
- Authority-backed placement sends the entity transform and typed initial
  template Facts in one revisioned `place_entity` command. Numbers and
  booleans remain typed values instead of being stored as canonical strings.
  Unity creates the presentation only from the accepted projection. A
  configured Authority client owns durable persistence even before login, so a
  pre-login local snapshot cannot silently restore a local-only monster or
  weapon into an Authority world.
- `Basic Slash Blue` and `Flash_round_ellow` are pooled presentation effects.
  Mixamo sword and Beholder clips are selected by canonical animation intent,
  not by gameplay code that searches FBX or Animator state names.
- A published action definition may declare a canonical
  `presentation.actorAnimationIntent`. The world projection exposes the intent
  for the exact package/action/definition version. After command acceptance,
  `OntologyAnimationAdapter` resolves that intent through the actor's
  `has_animation` repertoire and `AnimationDatabase`. Input/combat controllers
  never select clips or contain a fallback attack intent. Removing the
  presentation metadata or repertoire entry removes the animation without
  changing gameplay code.
- The executable Authority smoke proves equip, bounded damage, guarded death,
  ontology-data loot, command replay, package removal, and login/resume.
  Removing or disabling the combat package removes the matching authoritative
  action; there is no Unity fallback damage path.
- Equip input is an ephemeral Unity interaction request. A recent request may
  wait briefly while an Authority-first placement is projected into the scene,
  but the eventual equip still requires the canonical `Weapon` target and an
  accepted Authority action. Accepted animation presentation may likewise wait
  briefly for the versioned action projection, actor repertoire, and active
  Animator. Expired requests and idempotent replays do not trigger equipment
  or animation later.

## Related documents

- [Korean project context](PROJECT_CONTEXT.ko.md)
- [Project README](../README.md)
- [Local platform services](../infrastructure/README.md)
- [Ontology language-pack contract](../Assets/Resources/Ontology/Localization/README.md)
- [Actor animation handoff](../ACTOR_ANIMATION_HANDOFF.md)

## 22. Authority-confirmed equipment animation state

- Persistent equipped poses are Unity presentation derived from the
  Authority-owned `equipped_item` relation. They are not authored as a
  per-frame or duplicate durable `animation_intent` Fact.
- The equipped entity ID resolves to its canonical projected `templateId`.
  `CombatCatalog` must contain an explicit matching weapon presentation
  definition with explicit `idleAnimationIntent` and `moveAnimationIntent`.
  Runtime code never infers a stance from a weapon, prefab, mesh, or GameObject
  name.
- While stationary, the equipment idle intent replaces the Animator
  controller's default Idle. While moving, the equipment move intent selects
  the catalogued `WeaponWalk` presentation. Accepted transient actions
  override either persistent pose and return to the state selected by current
  movement after completion.
- Removing the `equipped_item` relation, removing the matching catalog
  definition, or clearing an equipment intent removes that presentation and
  restores the matching controller Idle/Locomotion. Schema defaults do not
  invent `WeaponIdle` or `WeaponWalk`.
- Actor sockets continue to follow the animated rig anchor every frame.
  Attachment pose authoring is therefore calibrated against the resolved
  equipment animation, rather than compensating for the default unarmed Idle.

## 23. Manifest-driven animation production line

- `AnimationContentManifest` is the project-owned authoring source for animation
  IDs, canonical intents, compatible actor and rig types, actor repertoires,
  playback layer, avatar mask, looping, root-motion policy, transition policy,
  provenance, delivery key, version, and checksum. `AnimationDatabase` and each
  actor profile's `has_animation` repertoire are generated projections.
- The first migration may seed the manifest from the legacy database. After
  that migration, synchronization is one-way from the manifest. Removing an
  entry cannot be undone by a stale runtime database, and synchronization also
  clears removed entries from known actor profiles.
- Ground movement, airborne/fall observation, landing, and equipped movement are resolved
  from ephemeral Unity observations by `OntologyAnimationStateResolver`.
  Per-frame animation state is never written as a durable world Fact. Accepted
  Authority actions remain the higher-priority source for transient attack and
  reaction intents.
- Manifest transition policy is executable metadata. Entries with
  `canBlend=true` use the normalized clip mixer; entries with `canBlend=false`
  cut immediately. Landing therefore cannot retain the airborne clip's
  vertical humanoid pose after the collision capsule has already touched the
  ground.
- Animation capability is not inferred from an FBX, clip, mesh, prefab,
  Animator state, or GameObject name. An actor may present an intent only when a
  compatible manifest entry exists in its explicit repertoire. Gameplay
  capabilities remain separate from animation metadata.
- Development action packages are validated against the animation manifest
  before publication. World entry requires the reloaded Authority projection
  to confirm the exact package ID, package version, action definition version,
  and animation intent; a warning cannot silently bypass this check.
- Project-owned import automation is limited to `Assets/Animations/` and
  approved UGC animation content. Third-party packages are read-only sources.
  User-submitted FBX files enter quarantine with attribution and license
  metadata, pass structural validation, and require explicit approval before
  moving into the approved manifest. Arbitrary runtime FBX loading is outside
  the Unity client and requires a separate trusted build/delivery service.

## 24. Package-scoped immutable action definitions

- An authoritative action definition is identified by the complete tuple
  `packageId + packageVersion + actionId + definitionVersion`. Reusing the same
  action ID and definition version in another immutable package version is
  valid and must not collide at the database layer.
- Non-action definitions retain their existing global identity until their
  world bindings also carry package identity. The package-scoped database
  constraint therefore applies only to `action_effect`.
- The combat Authority smoke runs against a new Docker project, network,
  PostgreSQL volume, and Redis volume by default. It applies all migrations,
  verifies cross-package definition reuse and the combat loop, then removes the
  containers and volumes. `-UseExistingServices` is an explicit diagnostic
  opt-in and must not be used by normal regression verification.
- Technical Authority rejection codes are written to the Unity Console.
  Account-facing UI resolves them through language-pack keys and does not expose
  raw transport or catalog messages.

## 25. Authority runtime foundation provisioning

- A newly created editable world receives at least one active runtime Zone
  through a revisioned `define_zone` command. The starter key, bounds,
  simulation mode, avatar movement speed, and readiness timeout are project
  data in `WorldAuthoritySettings`, not hidden gameplay constants.
- World entry repairs older editable worlds that have no Zone. It selects a
  non-dormant authored Zone, assigns the registered avatar through
  `save_avatar_checkpoint`, and authors `movement_speed` only when that Fact is
  missing. Existing authored values are preserved.
- Runtime repair never replaces an existing checkpoint transform. It preserves
  a valid saved Zone, or resolves a missing/retired Zone from the saved
  position, and changes only the Zone assignment when repair is required.
- Entry completes only after the Authority exposes an ephemeral motion state
  for the registered avatar in the selected Zone. Unity never substitutes its
  local Transform for an unavailable server position.
- The idempotent revisioned `migrate_legacy_equipment_relations` command
  retracts action-produced `equips` facts and an action-produced
  `actor attacks_with tool` relation when the matching
  `tool equipped_by actor` truth is absent. It never infers replacement
  equipment from action history.
  Canonical multi-slot equipment is item-owned
  `item equipped_by actor`; `actor equipped_item item` is legacy compatibility
  data and is retracted when that item is unequipped.
- `migrate_unassigned_entity_zones` assigns a legacy unzoned entity only when
  its durable X/Z position is contained by exactly one authored Zone. Ambiguous
  and out-of-bounds entities remain unassigned rather than being guessed into a
  runtime scope.
- Unity serializes revisioned durable commands per Authority client and retries
  one server-reported stale revision with a new idempotency key. Runtime motion
  remains ephemeral; this transport coordination does not create a gameplay
  rule or bypass a Rule Block.
- A proximity-constrained equipment command is not sent until the Authority
  runtime position is readable. Removing the runtime foundation removes the
  behavior instead of enabling a client-only fallback.

## 26. Player motion session convergence

- `movement_speed` is the authored maximum accepted by World Authority. A
  transient input sample carries the player's requested walk/run speed; the
  server clamps it to that authored maximum. Requested speed is not a Fact and
  does not advance the world revision.
- Entering a world explicitly activates the selected avatar's runtime motion.
  Activation clears the prior session's expiring input sequence and seeds Redis
  motion from the current durable avatar checkpoint/entity transform.
- An accepted `save_avatar_checkpoint` command re-seeds the matching ephemeral
  motion state from the committed durable transform. PostgreSQL remains the
  durable owner; Redis cannot preserve a contradictory older position.
- The motion scheduler advances only avatars whose owners have a live Zone
  session. Offline registrations no longer refresh motion TTL indefinitely.
- Unity keeps responsive local movement and never pulls it toward a
  low-frequency historical Authority sample while input is active. After
  local input stops, the client waits for an `idle` Authority state and a
  bounded settle delay, then smoothly reconciles any remaining horizontal
  error. It does not increase equip or attack range to conceal divergence.
- The input adapter remains the single owner of `CharacterController.Move`.
  Authority convergence contributes a bounded horizontal delta to that same
  movement call; it never performs a second controller move that could
  alternate grounding and collision resolution.

## 27. Authority combat targeting and deterministic presentation

- Combat input uses the project-authored `Player/Attack` Input System action.
  Camera look-hold remains a separate binding. Armed pointer input over a
  projected combat entity is consumed before click-to-move can create a
  conflicting navigation request.
- Unity raycasts only discover a candidate `OntologyAuthorityEntityIdentity`
  that exists in the current Authority projection. Unity does not decide
  hostility, damageability, permission, attack distance, damage, death, or
  loot.
- The attack command carries actor, target, tool, and the exact immutable
  action package tuple. World Authority validates the equipped relation,
  canonical target conditions, numeric tool damage, runtime positions, and
  authored maximum distance before committing mutations.
- Only an accepted command with a matching projected package/action/definition
  version may resolve `presentation.actorAnimationIntent`. Missing repertoire,
  missing Animator readiness, rejected actions, and unresolved metadata emit a
  diagnostic stage and never select a fallback clip.
- A weapon attachment profile may require a prefab-owned calibrated
  `OntologyAttachmentGripPoint`. For that profile, removing the marker removes
  the attachment presentation; the adapter does not fall back to the actor
  root, a guessed Humanoid bone, or a generic weapon offset. Legacy non-weapon
  profiles can retain the explicit generic profile pose.
- Restored avatar transforms enter a single presentation-preparation
  transaction before the in-world gate opens. The avatar root stays active
  while its renderers and input are gated; fixed-step collision grounding,
  stale input reset, and animation priming must all complete before `InWorld`.
  Swimming and mounted presentations are not force-grounded. A changed
  grounded pose is revision-confirmed as the avatar checkpoint, but the local
  grounding adapter creates no gameplay Fact or permission.

## 28. Ontology behavior contract and inflatable-ring baseline

- A gameplay vertical slice is complete only when authored Triples identify its
  semantic and physical profiles, an assigned Rule Block enables it, and a
  generic physical/presentation adapter expresses the evaluated result.
  Removing the enabling Rule Block must retract the behavior; Unity must not
  restore it with an input, prefab-name, or object-name fallback.
- The registered world avatar owns the durable canonical
  `has_concept -> Actor` world Fact. New avatar placement authors it
  atomically with the entity, and world-entry foundation repair adds it to
  older editable worlds before Rule Block evaluation. Account profile data
  remains outside world Facts.
- The development content workflow publishes the immutable Rule definitions
  required by catalog defaults before it publishes object Rule Block bindings.
  A binding ID is deterministic from entity, rule, and binding variable, so a
  replay cannot create a duplicate behavior contract.
- Entities created before this contract are repaired once from their catalog
  defaults. The durable `semantic_contract_version` marker is written only
  after every default binding succeeds. Once marked, a user removing a Rule
  Block is authoritative and world entry does not add it again.
- Local proximity presentation resolves the account avatar or authored player
  input role instead of assuming that `Actor` is unique. NPC and monster Actors
  therefore do not disable wearable proximity.
- The inflatable ring is the reference slice: its wearable/skill and profile
  Triples, `AutoEquipNearbyWearable` and related Rule Blocks, and
  `LightBuoyant`/waist attachment profiles jointly produce attachment and the
  temporary Swimming skill after the nearby actor explicitly presses `F`.
  A clean evaluation without the equipment Rule
  Block cannot produce the slot relation, attachment, or temporary skill.

## 29. Rule-Block-gated equipment

- Every catalog weapon now carries the same complete contract: canonical
  `Weapon`/`Carryable` and equipment Triples, the assigned
  `EquipItemOnInteractionIntent` Rule Block, the `RightHandCarry` attachment
  profile, and the `HandheldWeapon` physical profile.
- The immutable `equip_weapon` action transports a canonical ephemeral
  `interaction_intent`; it has no direct `equipped_by` effect. World Authority
  resolves the exact active `EquipItemOnInteractionIntent` binding and evaluates
  that published Rule Definition in the command snapshot. Removing the binding
  therefore removes the behavior. Unity and the transport action do not own an
  alternative equipment result.
- Durable results produced by command-scoped rules record their
  `source_rule_binding_id`. Removing that Rule Block or its Meaning Package
  retracts its owned results rather than leaving a hidden action fallback.
- Catalog weapons use semantic contract version 2. Existing placed weapons move
  from `AutoCarryNearbyCarryable/?object` to
  `EquipItemOnInteractionIntent/?target` only through the catalog-authored
  migration; intentional user removal is preserved.
- The development package publishes its current tube/equipment Rule Blocks as
  immutable catalog version 2. Development publication and new world bindings
  resolve the same versions. Changed Rule Block content must advance its
  catalog version instead of overwriting version 1.
- `HandheldWeapon` is project-owned physical tuning for a dynamic world item.
  Attachment temporarily transfers Rigidbody/collider presentation ownership;
  it does not replace the physical-profile Fact or encode weapon identity in a
  prefab name.
- Legacy editable worlds receive missing catalog Triples and default bindings
  through revisioned, idempotent commands before the semantic-contract marker
  is written. After the marker exists, later user removal remains authoritative
  and world entry does not recreate the Rule Block.
- Development package `1.9.0` publishes the required rule definitions before
  binding them, `equip_weapon` definition version `7`,
  `equip_wearable` version `1`, and `unequip_equipment` version `1`. Older
  package/action versions cannot silently satisfy the new equipment contract.

## 30. Exclusive runtime Transform ownership

- A Unity Transform has exactly one presentation owner at a time. The priority
  is world-editor manipulation, attachment, local character controller or
  actor runtime, Dynamic physics, then durable Authority projection.
- This ownership is ephemeral presentation state derived from explicit
  ontology profiles and active relations. It is not a durable Fact and is never
  inferred from a prefab, mesh, definition, or GameObject name.
- An `Anchored` or `AuthorityKinematic` physical profile remains kinematic and
  follows its Authority/runtime owner. A `Dynamic` profile is seeded once from
  the durable projection, then Unity physics owns its live pose. Later
  projection refreshes cannot snap the active Rigidbody back to an older pose.
- Dynamic physics does not publish per-frame transforms. After a meaningful
  movement has remained settled for a bounded interval, an editable Authority
  client may publish one revisioned, idempotent `move_entity` checkpoint.
- Catalog entries must select a physical profile explicitly. A missing profile
  creates no Rigidbody behavior; the former `HeavySinking` catalog fallback is
  not a runtime policy. Current vegetation and landmarks are
  `StaticAnchored`, the Beholder uses `AuthorityKinematic`, stones explicitly
  use `HeavySinking`, weapons use `HandheldWeapon`, and the ring uses
  `LightBuoyant`.
- Placement resolves the final prefab, ontology template, physical profile, and
  generated Collider before calculating support height. Rigidbody velocity is
  cleared only after that final collision shape is aligned.
- Avatar checkpoint restoration remains Authority-owned durable data. Unity
  performs one capsule-safe support cast through `CharacterController.Move`,
  clears ephemeral vertical velocity, and does not disable/re-enable the
  controller or rewrite the checkpoint.
- During an entered session, the local avatar keeps ownership of its visible
  runtime pose even if semantic synchronization temporarily disables or removes
  the locomotion motion lease. The missing lease stops movement; it does not
  authorize a stale durable projection to snap the avatar back. Only explicit
  entry, recovery, and respawn lifecycle adapters may restore a durable pose.
- Support-probe proximity is collision observation, not permission to climb.
  Explicit step rise requires an actual grounded `CharacterController` contact.
  A surface inside `slopeLimit` remains continuous `WalkableSupport`; horizontal
  locomotion follows its tangent while gravity and grounded adhesion remain
  world-vertical. A support-normal adhesion vector is forbidden because its
  planar component causes backward slope drift.

## 31. Persistent Unity MCP development transport

- Local Codex/Unity development uses one Streamable HTTP MCP endpoint at
  `http://127.0.0.1:8080/mcp`. Per-task stdio servers are not the project
  default because Unity assembly reloads temporarily close their editor-owned
  TCP listener and can race port discovery and framed handshakes.
- MCP for Unity runs its local HTTP server independently of the editor domain.
  Unity connects to it through the HTTP/WebSocket bridge with `Auto-Start on
  Editor Load` enabled. An assembly reload may make the Unity instance
  temporarily absent, but the bridge must reconnect without a manual Start
  Session click.
- `scripts/verify-unity-mcp.ps1` verifies server health, the expected connected
  Unity project, and the Codex HTTP configuration. Run
  `scripts/verify-development.ps1 -RequireUnityMcp` when the editor is expected
  to be open.
- Logs from `com.unity.ai.assistant` relay ports 9001/9002 are a separate Unity
  AI service and are not evidence that MCP for Unity disconnected.
- This is development infrastructure only. It owns no account data, world
  Fact, Rule Block, physical meaning, or runtime gameplay authority.

## 32. Normalized ontology animation transitions

- Canonical animation intent and actor repertoire continue to select clips.
  Unity presentation blends the selected result; it does not create movement,
  combat permission, or a durable world Fact.
- Full-body clip-to-clip transitions use one normalized
  `AnimationMixerPlayable` beneath a single full-body
  `AnimationLayerMixerPlayable` layer. Consecutive clips must not be placed on
  separate override layers with complementary weights: layer composition is
  sequential rather than a normalized cross-fade and can leak the controller
  or bind pose into the avatar.
- Controller-to-clip and clip-to-controller transitions remain layer-weight
  transitions. Clip-to-clip transitions keep the ontology presentation layer
  fully weighted and cross-fade only inside its normalized clip mixer.
- Removing an animation intent or repertoire entry still removes the selected
  presentation. This transition topology is only a presentation adapter and is
  not a hidden fallback for missing Triple, Rule Block, or Authority results.

## 33. Authority-owned meaning package changes

- A meaning package is one durable, revisioned, idempotent authoring command
  that changes the selected entity's authored Triples, Rule Block bindings,
  and Physical Meaning declaration together. Unity no longer commits these
  coupled values as separate local edits.
- Runtime Quick Setup is the normal player-facing application surface for a
  meaning package. Its choices come from `RuleBlockPresetDatabase`; adding a
  new choice means publishing data for the required concepts, authored Facts,
  Rule Blocks, validation prerequisites, and optional Physical Meaning rather
  than adding another Unity button or ID-specific branch.
- Meaning packages are valid for any placed entity. The Authority does not use
  template, prefab, mesh, concept, or object-name compatibility branches to
  decide whether a user may give an object a new meaning.
- Each package occupies an explicit semantic slot such as
  `primary_physical_meaning`, `attachment_meaning`, or a named additive
  physical-effect slot. Applying another package to the same slot atomically
  removes the prior contribution, restores its displaced authored baseline,
  then applies the new contribution in the same world revision.
- The Authority contribution ledger records the Fact and Rule Block rows owned
  by that package plus the authored rows it displaced. A package may explicitly
  adopt exact, active, unclaimed rows that predate package ownership; it can
  never claim a row owned by another active package.
- When an unclaimed legacy Rule Block has the same rule and parameters but an
  older immutable definition version, baseline adoption atomically retracts
  that binding and its inferred results, then inserts the requested version
  with a fresh binding identity. Immutable history is preserved and duplicate
  active versions are not left behind.
- Catalog-preconfigured entities use the data-derived
  `template_semantic_baseline` package. It owns the definition's initial
  concepts/Facts, Physical or Attachment profile declarations, and only the
  default Rule Blocks that are still active. Existing placements adopt this
  baseline once when their semantic contract version advances, so a previously
  removed default is not silently restored.
- Removing one package-owned Rule Block retracts only that binding and its
  `RuleBound` results. Sibling bindings and shared authored meaning remain
  active. Removing the final owned Rule Block closes the package and retracts
  its owned canonical and typed Triples plus profile declarations in the same
  Authority revision. Independently authored values and other packages remain.
  The durable semantic-contract marker prevents world entry from resurrecting
  a removed binding or baseline.
- Unity is command-first for runtime presets, physical profiles, attachment
  profiles, temporary-skill grants, and physical effects. It waits for the
  accepted Authority projection, then synchronizes semantic adapters for each
  changed entity before running local simulation/presentation.
- A rejected package changes nothing. A stale projection cannot leave semantic
  data and Unity physics adapters in different states because projection apply
  always re-runs the semantic adapter synchronizer for changed objects.
- The persistent Authority bridge may become active before the additive World
  scene creates its authoring controllers. Dependency discovery and event
  subscription are therefore one idempotent operation: a controller discovered
  by a later projection or interaction is subscribed immediately. A local Rule
  Block or meaning-package edit must never appear briefly and then disappear
  merely because the authoring scene loaded after the bridge.

## 34. Remote-avatar local identity exclusion

- Remote-avatar presentation excludes the account-entry avatar identity first,
  then the actor that owns local player input. It never selects the first
  `OntologyAuthorityEntityIdentity` found in the scene.
- A carryable, NPC, monster, or placed object can have the same generic
  Authority identity component, so component discovery order is not evidence
  that an entity is the local player.
- If the stable local avatar becomes available after the remote presenter,
  the presenter replaces any stale cached identity and immediately removes a
  presentation-only replica with that local avatar ID.
- Remote replicas remain ephemeral Unity presentation. This correction does not
  create or remove world Facts, motion snapshots, account profiles, or durable
  world entities.

## 35. Recoverable meaning-package outbox contract

- Optional entity GUIDs in Authority command JSON use JSON `null`, never an
  empty string. Canonical meaning-package Facts therefore publish
  `objectEntityId: null`.
- Unity normalizes legacy pending `apply_meaning_package` commands before
  replay while preserving their original command IDs. Authority idempotency,
  rather than a new local mutation, decides whether each recovered command is
  applied.
- A malformed meaning-package payload is a completed Authority rejection with
  `invalid_meaning_package_payload`; it must not escape as HTTP 500 and appear
  to Unity as a lost transport response.
- World entry may wait for genuinely unresolved transport failures, but one
  malformed historical outbox record must not hold the session permanently in
  `Recovering`. Replayed or rejected completed commands leave the outbox.

## 36. Authority-aligned drowning recovery checkpoint

- `Drowning` remains an inferred movement relation. Its presence enables the
  Unity recovery presentation; removing the relation immediately removes that
  presentation. Unity does not infer drowning from a water mesh or object name.
- The last accepted durable avatar checkpoint is the fixed respawn anchor.
  Ordinary movement, water overlap, falling, and the sinking presentation do
  not move this anchor. Automatic checkpoint capture accepts only stable dry
  ground presentation.
- Checkpoint and account-entry services resolve the avatar through the actor
  that owns local player input. Component discovery order is never allowed to
  select an NPC, monster, carryable, or placed entity as the checkpoint owner.
- Recovery teleports the local avatar to the confirmed anchor, clears ephemeral
  vertical motion, and requests one collision grounding pass. It then sends one
  revisioned, idempotent `save_avatar_checkpoint` command for that recovered
  pose.
- While the recovery checkpoint is pending, horizontal motion reconciliation
  is suspended. An accepted checkpoint re-seeds the Authority's ephemeral
  motion state from the same durable transform before reconciliation resumes,
  so a stale water position cannot pull the avatar back toward the drowning
  location.
- Recovery creates no authored gameplay Fact and does not save per-frame
  motion. If the `Drowning` relation is absent or removed, the recovery adapter
  has no authority to move the avatar.

## 37. Equipment interaction and multi-slot ownership

- Wearables and carried weapons intentionally use separate input policies.
  `pickup_behavior -> SelectThenEquip` uses the established select/approach
  flow: left-click selects the wearable, the approach publishes the ephemeral
  `interaction_intent`, and `AutoEquipNearbyWearable` derives attachment only
  after the actor is near. Clicking the attached wearable runs its ontology
  unequip action.
- `F` is reserved for `pickup_behavior -> SelectThenCarry` and sends the
  immutable `equip_weapon` Authority action. Unity never writes weapon
  permission Facts or decides from a prefab, mesh, or object name.
- Both paths still require the target's authored concept/profile Triples and
  the matching `AutoEquipNearbyWearable` or
  `EquipItemOnInteractionIntent` Rule Block.
- Durable equipped state is item-owned:
  `item equipped_by actor`. Every equippable item authors exactly one
  `has_slot` value. The generic `EquipmentSlotAvailable` ontology condition
  rejects a second item when another item with the same slot is already owned
  by that actor; it does not inspect slot, item, prefab, mesh, or object names.
- Different slots coexist. The reference pair is
  `InflatableRing has_slot Waist` plus `Sword has_slot RightHand`. Equipping or
  removing either item changes only its own relation, so the other item's
  attachment, physical meaning, temporary skill, and animation selection
  remain intact.
- Pressing `F` on an equipped weapon runs `unequip_equipment`. A flotation
  wearable is excluded from the combat input's candidate and fallback lists,
  so `F` cannot attach or remove the tube.
- Attachment adapters present `equipped_by`; the combat animation resolver
  filters the equipped items through the data-owned weapon presentation
  catalog. A flotation wearable therefore does not select a weapon idle, and
  removing weapon presentation metadata exposes no fallback animation.
- Removing the matching equipment Rule Block removes its behavior. Wearable
  proximity attachment cannot derive without `AutoEquipNearbyWearable`, and
  Authority rejects weapon equipment without
  `EquipItemOnInteractionIntent`. Neither path has a name-based permission
  fallback.

## 38. Authority projection membership and semantic-contract retirement

- After world entry, the current World Authority projection is the membership
  source for runtime placeable presentation. A Unity placeable with a durable
  Authority GUID that is absent from the projection is disabled and removed;
  it cannot remain visible as a non-interactable local ghost.
- Authority-first placement presentations are protected while their revisioned
  placement command is in flight. Edit-mode and pre-entry local authoring are
  not affected by runtime projection reconciliation.
- Rule Block replacement and retirement are separate catalog-owned operations.
  A migration may add a replacement only while crossing the version that
  introduced it. A later retirement removes an obsolete binding only while
  crossing its own contract version.
- Weapon semantic contract version 3 retires
  `AutoCarryNearbyCarryable/?object`. Existing version-1 weapons first receive
  `EquipItemOnInteractionIntent/?target`; existing version-2 weapons only lose
  the stale automatic binding. A user removal of the new F-interaction block
  is never silently restored.
- Re-adding a retired reusable block after version 3 remains an explicit user
  authoring decision. Runtime code does not continuously police or recreate
  Rule Blocks after the one-time data-authored contract transition.

## 39. Optional effect metadata across Unity and Authority

- Unity serialization can materialize an unconfigured optional nested object
  as a default instance. In published Rule Definitions this may appear as an
  empty numeric source or a numeric guard whose comparison is `None`.
- Authority treats a completely empty default numeric source or guard as
  absent. It validates and evaluates the object only when at least one of its
  semantic fields is configured. Partially configured metadata remains invalid
  and is rejected.
- This wire-boundary rule is generic effect semantics. It does not special-case
  equipment, a predicate, a Rule Block ID, a prefab, or a Unity object name.
- Existing immutable definitions remain executable without rewriting their
  payload or checksum. A genuine configured numeric source/guard keeps the same
  strict validation and evaluation behavior.
- The same compatibility boundary applies to optional Rule Block presentation.
  Unity may serialize an unconfigured `runtimePresentation` as an empty nested
  object. Authority canonicalization treats that empty object as the legacy
  absent field, so adding the schema field cannot change the checksum of every
  previously published immutable rule.
- A configured runtime presentation such as `AttackLight` remains part of the
  immutable Rule Block payload. Changing a configured value requires a new
  definition version; only an empty schema placeholder is normalized away.

## 40. Interruptible action presentation yields to locomotion

- Authority approval still selects transient action presentation through the
  action definition's canonical animation intent. Unity does not infer an
  equipment animation from the weapon, prefab, mesh, or object name.
- The animation manifest owns whether a transient clip is interruptible.
  When an interruptible transient presentation is active and the player emits
  a movement intent, the presentation adapter immediately resolves the current
  equipment locomotion intent. It does not wait for an idle frame first.
- `WeaponEquip` and `WeaponUnequip` are interruptible so movement can transition
  directly to the catalog-selected `WeaponWalk`. Attacks, reactions, and other
  entries authored as non-interruptible retain their presentation lock.
- This is ephemeral presentation scheduling only. It does not author a world
  Fact, change Authority equipment state, or bypass Triple, Rule Block,
  Physical Meaning, and animation-repertoire requirements.

## 41. Manifest-first animation wizard production line

- `AnimationContentManifest.asset` is the sole authoring source for reusable
  animation content. `AnimationDatabase.asset` and each ActorProfile animation
  repertoire are generated projections, not independent authoring surfaces.
- The character animation wizard creates a canonical manifest entry first,
  including clip provenance, intent, actor and rig scope, playback policy, and
  assigned profiles. It validates the complete candidate manifest before
  committing the entry.
- A successful validation automatically synchronizes the runtime Animation
  Database and every affected ActorProfile. A failed validation restores the
  previous manifest and does not leave a Database-only or Profile-only entry.
- Removing an entry from the manifest removes its generated runtime definition
  and profile membership on the next synchronization. The wizard must not
  inject hardcoded semantic properties such as `Swimming` or `Locomotion`.
- Existing base, equipment, attack, and recovery animations are verified
  against this same projection contract. World Authority development action
  animation intents must resolve in the synchronized manifest before package
  publication.

## 42. Rule-Block-owned primary melee attack

- `Player/Attack` shares left mouse click with click-to-move. Unity reports only
  the pointer candidate and the equipped tool's authored action IDs. World
  Authority previews the same published actions and assigned Rule Blocks used
  by execution, without applying mutations or acquiring cooldown. Only an
  accepted living hostile inside the authored `attack_range` consumes the
  click; empty ground, friendly entities, dead targets, missing Rule Blocks,
  and out-of-range targets remain navigation input. Right mouse is
  intentionally left available for camera or a future secondary action.
- A weapon that can swing and attack authors `swing_action`, `attack_action`,
  `attack_damage`, `attack_range`, `attack_cooldown`,
  `grants_capability -> MeleeAttack`, and compatible physical/attachment
  profiles. The action IDs and tuning travel with weapon data instead of a
  prefab, mesh, or object name.
- `SwingWeaponOnPrimaryIntent/?tool` owns the canonical `AttackLight` runtime
  presentation and has no durable effects.
  `MeleeAttackOnPrimaryIntent/?tool` separately owns health, death, and loot
  mutations. Neither immutable transport duplicates the result owned by its
  assigned Rule Block.
- The side-effect-free action-preview endpoint evaluates
  `primary_attack_intent` and `primary_swing_intent` through the same prepared
  Rule-Block path as execution. Preview may describe future mutations but does
  not apply them, acquire cooldown, write an event/Fact, or advance revision.
  After both previews accept, the swing uses the presentation-only runtime
  action endpoint and the same target uses the existing revisioned, idempotent
  damage action. Execution revalidates the contract, so preview never becomes
  an authority grant.
- Position samples and cooldown leases remain ephemeral. No per-frame or empty
  ground-click Fact/event is written. Preview requests occur only for a
  projected pointer candidate while an authored attack tool is equipped.
  Redis supplies the cross-replica cooldown lease when services are enabled.
- Unity animation and VFX adapters consume only the accepted Authority
  presentation intent. The validated animation manifest, actor repertoire,
  combat catalog, and VFX data resolve visuals but never create combat
  outcomes.
- Weapon semantic contract version 4 introduces target-attack data once;
  version 5 introduces `swing_action` and its Rule Block once. Existing authored
  values and later user removals are preserved. Removing either required block
  removes the combat click route without creating a hidden fallback; the same
  click remains available to movement. Attack action version 9 and swing action
  version 1 are synchronized across the Authority package, weapon manifest,
  and combat catalog.

## 43. Portable autonomous actor and monster contract

- A mesh, prefab, placement category, and object name do not make an entity a
  monster. Any placed entity, including a plain log, becomes an autonomous
  melee actor only when authored data supplies the complete reusable contract:
  `Actor`, `AutonomousAgent`, `Monster`, `Combatant`, and `Damageable`
  concepts; faction, hostility, life, movement, detection, leash, attack,
  action, and animation-intent Facts; an assigned autonomous combat Rule
  Block; and `AuthorityKinematic` Physical Meaning.
- `autonomous_melee_monster` is an editor preset for attaching that contract to
  the selected entity. It contains no executable Unity logic and does not
  modify the source prefab or template. Users may tune or replace the authored
  Facts and reusable rule after applying it.
- Catalog-authored starter monsters use semantic contract version 3. Legacy
  placed instances with no marker receive the complete current contract once;
  versioned instances receive only the declared Fact and Rule Block
  introductions or retirements crossed by that version transition. Version 3
  retires the obsolete authored `current_health=30` and `is_alive=true`
  defaults only when another active value for the same predicate exists. This
  removes a conflicting legacy default without reviving a dead monster or
  deleting its only valid state. After the marker advances, user removal
  remains authoritative and no later refresh silently restores the behavior.
- Relation cardinality used by contract migration is canonical term metadata,
  not a Unity name exception. A set-valued relation such as `has_concept`
  suppresses only an exact duplicate, so an existing `Monster` concept does
  not prevent introduction of `AutonomousAgent` or `Combatant`. Single or
  unspecified relations preserve an existing user-authored value. The same
  cardinality rule applies to unmarked legacy repair; it must not append a
  catalog default beside an existing single-valued runtime or authored state.
- `attack_action` selects an enabled immutable action definition. That action's
  declared Rule Block must match an active binding on the actor; the scheduler
  does not recognize a fixed monster prefab or manufacture a hidden combat
  fallback.
- World Authority owns target selection, faction eligibility, range, leash,
  movement simulation, cooldown, damage, death, revision, and idempotency.
  Autonomous movement and presentation timing remain ephemeral; accepted
  health/death changes are durable Authority mutations.
- Unity's `AuthorityKinematic` adapter only interpolates the accepted actor
  pose and presents the canonical idle, movement, or attack animation intent.
  It neither searches for players nor evaluates hostility, range, damage, or a
  Rule Block.
- Removing the assigned Rule Block, the action-to-rule link, a required
  semantic Fact/concept, or `AuthorityKinematic` meaning removes autonomous
  behavior on the next configuration refresh. The object remains an ordinary
  placed object and no name-based fallback restores monster behavior.

## 44. Rule-Block-owned player combat respawn

- Combat death recovery is separate from drowning recovery. A player avatar
  authors `respawn_action -> respawn_avatar`, and the assigned
  `RespawnPlayerOnDeath` Rule Block owns the durable health and life-state
  transition.
- World Authority evaluates the action against the current revision and
  restores `current_health` from `maximum_health` before setting
  `is_alive=true`. Unity does not write either result.
- After Authority accepts the action and the refreshed projection confirms the
  avatar is alive, Unity presents the already-confirmed checkpoint pose,
  resets ephemeral grounding/motion state, and re-seeds the same pose once.
  The checkpoint adapter does not own the respawn rule.
- Player semantic contract version 1 introduces the action Fact and Rule Block
  binding once. The marker prevents later refreshes from silently restoring a
  Rule Block that the user removed.
- Removing `RespawnPlayerOnDeath` makes Authority reject the respawn intent.
  Unity has no hidden health, life-state, or position fallback.
- The reusable contract is published in development action package
  `social_village@3.2.1`.

## 45. Harness-enforced weapon and monster production contracts

- A weapon is not production-ready because a model can be equipped or an
  animation can play. The mandatory `weapon-ontology-production` contract
  requires this order: project-owned resource registration; capability,
  action-ID, slot, damage, range, cooldown, and profile Triples; assigned
  equipment/swing/attack Rule Blocks; immutable actions invoking the matching
  blocks; Physical Meaning and attachment/grip profiles; validated animation
  and VFX manifest content; Authority evaluation and durable mutation; Unity
  presentation of accepted results; and executable Rule-Block-removed
  evidence.
- A monster is not production-ready because a prefab moves or attacks. The
  mandatory `monster-ontology-production` contract requires this order:
  project-owned visual/catalog registration; actor, autonomous-agent, monster,
  combatant, damageable, faction, hostility, life, movement, detection, leash,
  attack, action, and animation-intent Triples; an assigned autonomous Rule
  Block; an immutable action invoking that block; `AuthorityKinematic`
  Physical Meaning; validated animation manifest/profile content; Authority
  simulation and durable combat mutation; Unity interpolation/presentation;
  and executable removed-contract evidence.
- `scripts/verify-development.ps1` rejects the harness manifest when either
  canonical production contract is absent, duplicated, renamed, incomplete,
  out of version, lacks forbidden-implementation declarations, or is not
  attached to a gameplay-pipeline scenario with enabled and removed evidence.
- The forbidden implementations include mesh/prefab/object-name gameplay
  branches, Unity-owned gameplay outcomes or direct durable writes, action
  effects that duplicate assigned Rule Block results, behavior that survives
  Rule Block removal, and animation registration that bypasses the validated
  manifest.
- Every new or migrated weapon or monster must extend its canonical contract
  and executable evidence before it can be considered reusable production
  content.

## 46. Harness-enforced player ontology production contract

- Player gameplay now follows the mandatory `player-ontology-production`
  contract. Account-owned appearance and profile preferences remain outside
  world Facts; the selected world avatar owns its durable player role,
  capability, life, faction, action, movement, Physical Meaning, and animation
  intent Triples.
- Player semantic contract version 2 introduces
  `locomotion_action -> move_avatar`, `grants_capability -> Locomotion`,
  numeric `movement_speed`, `physical_profile -> AuthorityKinematic`,
  canonical idle/movement animation intents, and the assigned
  `MovePlayerFromIntent/?actor` Rule Block. The package is published as
  `social_village@3.3.0`.
- Each transient movement sample identifies the immutable locomotion action.
  World Authority previews that action through its matching assigned Rule
  Block before accepting the sample. The scheduler independently requires the
  same actor/player concepts, capability, life state, Physical Meaning,
  action-to-rule link, enabled binding, and numeric speed.
- Unity input remains the trigger and local collision/presentation owner. In an
  Authority world it may present predicted locomotion only after Authority
  accepts the current contract. Removing the locomotion action disables the
  Unity route; removing the Rule Block or another required semantic makes
  Authority reject the input and removes the avatar from motion scheduling.
- Entry migration adds only semantics introduced after the avatar's recorded
  contract version. The runtime foundation no longer reauthors all default
  player Facts on every entry, so user removal after migration remains
  authoritative.
- The player production evidence covers complete and removed locomotion,
  existing equipment and primary attack contracts, combat death/respawn, the
  validated animation manifest, and the shared Authority/Unity boundary. A
  full live hunting pass is the next product-level acceptance test after this
  automated contract verification.

## 47. Target-locked primary combat approach

- A left click that observes a projected, living combat target is reserved as
  an ephemeral combat intent while World Authority previews the authored
  attack and swing actions. The same click must not become a terrain movement
  command merely because another attack is in flight or cooldown is active.
- If Authority rejects only with `action_target_out_of_range`, Unity may
  navigate toward the observed target. Its stopping radius comes from the
  equipped tool's canonical numeric `attack_range` Triple with a small
  navigation-only inset. Unity does not invent attack permission, range,
  hostility, life, cooldown, damage, or animation results.
- Reaching the stopping radius causes one new Authority preview. Accepted
  preview executes the existing ephemeral swing and revisioned damage paths.
  Busy and cooldown responses wait without movement. Target death, removal
  from projection, direct keyboard movement, UI capture, or component disable
  clears the ephemeral approach.
- Missing Rule Blocks, action definitions, required semantics, or invalid
  range data do not activate approach. The rejected click returns to the
  ordinary non-combat route, so removal of the combat contract still removes
  combat behavior without a hidden Unity fallback.

## 48. Contract-scoped locomotion approval and attack transition

- A world revision is not itself a player locomotion contract change. Unity
  preserves an already accepted transient locomotion lease when a projection
  changes only unrelated entities, such as a monster's health after damage.
- The local locomotion contract fingerprint contains the world and Zone scope,
  the avatar's own Facts, avatar-targeted Rule Block bindings, and the unique
  immutable locomotion action definition. A missing, ambiguous, removed, or
  changed contract invalidates prediction and requires a new Authority
  acceptance.
- Animation interruption remains authored in
  `AnimationContentManifest`. The current `AttackLight` presentation explicitly
  yields to an approved locomotion intent, so Unity does not move the avatar
  while retaining a non-walking attack pose. A manifest entry authored as
  non-interruptible still retains its presentation lock.
- This presentation transition does not grant movement or attack permission,
  change damage, or create a Fact. Authority continues to evaluate the
  locomotion and attack actions through their assigned Rule Blocks; the
  animation adapter only chooses how an accepted result is presented.

## 49. Durable entity retirement and defeated-state boundary

- `is_alive=false` is a durable gameplay state, not an instruction for Unity to
  delete a GameObject. A defeated entity can still be presented as a corpse,
  expose loot, or participate in an authored lifecycle.
- Permanent removal uses the revisioned, idempotent World Authority command
  `retire_entity`. In one revision the Authority sets the entity's
  `deleted_revision`, retracts active Triples that describe or reference it,
  retracts its Rule Block bindings, and closes active meaning-package
  applications. Historical rows and the accepted command/event remain
  available for audit.
- Unity's world editor never destroys an Authority-projected entity first. It
  submits the retirement request, reloads the accepted projection, and removes
  the local presentation only when the entity is absent from that projection.
  Local-only previews retain a local deletion fallback.
- Account-owned player avatars are protected from this generic authoring
  command because their lifecycle is owned by account/avatar operations.
- Defeat does not secretly trigger retirement. Automatic corpse cleanup must be
  introduced later as an explicit reusable lifecycle Rule Block/action with
  authored conditions such as defeat and loot completion. Removing that block
  must preserve the defeated entity.
- While the defeated state remains projected, Unity presents the canonical
  death intent as a persistent state presentation and holds the final frame of
  its validated non-looping clip. Idle, locomotion, or autonomous motion
  presentation cannot make the defeated entity appear alive again. Clearing
  the defeated state or retiring the entity releases that presentation.
- No template, prefab, mesh, display-name, monster-name, or object-name branch
  participates in retirement. Any ontology-configured world entity follows the
  same Authority command and projection path.

## 50. Authority-previewed, contact-delivered melee impact

- Primary melee damage is no longer executed immediately after target
  selection or swing approval. World Authority first previews the authored
  attack and swing actions; preview creates no durable mutation.
- A weapon authors
  `attack_contact_mode -> WeaponContactWindow`. The assigned
  `MeleeAttackOnPrimaryIntent` Rule Block requires that Triple. Removing the
  Triple or Rule Block removes damage instead of activating a Unity fallback.
- `AnimationContentManifest` owns the normalized contact window for the
  approved `AttackLight` clip. The project-owned weapon prefab owns an
  adjustable contact volume. Unity observes overlap only inside that window
  and only against the target selected by the accepted Authority preview.
- Click navigation uses the shorter of the Authority-authored attack range and
  a stop distance derived from the weapon/target collider geometry. This
  steering calculation creates no damage and exists only to bring short
  weapons into a physically reachable position.
- An observed overlap is ephemeral input timing, not damage authority. It
  causes Unity to submit the existing revisioned action; World Authority
  re-evaluates the complete rule, range, cooldown, equipment, hostility and
  life contract before changing health, death or loot.
- Impact VFX and hit reaction use the observed physical contact point only
  after the durable action is accepted and its projection is refreshed. A
  cursor raycast point, elapsed-time fallback, prefab name or animation name
  must not create an impact.

## 51. Motion-oriented weapon sweep presentation

- Swing VFX no longer starts at swing acceptance with one fixed prefab
  rotation. It starts inside the validated animation contact window.
- The weapon presentation adapter observes the authored blade axis and the
  actual frame-to-frame movement of its editable slash anchor. Their plane
  determines a dynamic VFX anchor, so opposite or differently angled swings
  orient the effect with the animation.
- The catalog's local VFX rotation remains an editable art offset after the
  motion-derived orientation. No weapon, prefab, mesh or animation name branch
  participates in orientation.
- Sweep motion is ephemeral Unity presentation. It cannot create contact,
  damage, cooldown, death or loot. The Authority-approved swing Rule Block is
  still required to publish the animation intent; removing it produces no
  hidden sweep fallback.

## 52. Direction-invariant targeted melee delivery

- A targeted click rotates the player toward the selected projected target
  before publishing the attack intent. Facing is ephemeral navigation and does
  not replace Authority target, range, hostility or Rule Block evaluation.
- Contact observation retains the previous weapon Box pose and samples the
  interpolated translation and rotation to the current pose. A fast sword can
  therefore no longer cross a target between rendered frames without being
  observed.
- Linear/angular sample resolution and the safety cap are editable adapter
  settings on the project-owned weapon prefab. They are generic geometry
  settings; no world direction, weapon name, monster name or animation name is
  special-cased.
- Interpolated contact still only qualifies the already approved target inside
  the manifest contact window. World Authority remains the sole owner of the
  resulting health, cooldown, death and loot mutation.

## 53. Oriented-surface melee approach

- Contact approach no longer treats the three-dimensional diagonal of a weapon
  Box as reach in every horizontal direction. The adapter projects the
  currently oriented authored contact volume toward the approved target's
  closest collider point and derives a conservative navigation stop distance
  from those actual surfaces.
- Before the manifest contact window opens, the contact adapter continuously
  primes the previous weapon pose without reporting impact. The first approved
  in-window sample therefore sweeps only from the adjacent rendered pose and
  does not miss a target crossed at the window boundary.
- Combat navigation also derives a minimum planar body clearance from the
  player CharacterController and the approved target's authored interaction
  collider. The attack-range navigation inset may move the player inside the
  Authority range, but it may never consume this physical clearance or place
  the two presentation bodies inside each other.
- These calculations are ephemeral Physical Meaning presentation. They contain
  no weapon, prefab, animation, monster, or world-direction exception and can
  never create damage. Missing contact, the contact-mode Triple, the animation
  window, or the assigned attack Rule Block still removes the damage path.

## 54. Combat-pointer surface disambiguation

- Pointer targeting checks all ray hits in distance order and selects only a
  projected entity that exposes the combat-target presentation contract.
  A nearer projected non-combat entity no longer hides a valid combat candidate
  behind it.
- When a pointer ray narrowly misses the target trigger and reaches terrain
  directly beneath it, the targeting adapter may recover the projected combat
  candidate from the authored interaction-collider footprint plus a small,
  editable presentation padding. The adapter also requires an unobstructed
  camera line, so terrain elsewhere and targets behind solid geometry remain
  ordinary movement input.
- This recovery is an ephemeral pointer observation, not combat permission.
  Hostility, life state, range, cooldown, assigned Rule Blocks, animation
  intent, contact, and damage are still evaluated by World Authority. Removing
  the required Rule Block therefore still returns the click to movement with no
  Unity fallback.

## 55. Converged player, weapon, monster, and hunting contract

- Equipment, player motion, autonomous combat, defeat, and loot now follow one
  traceable path:
  `authored Triple -> assigned Rule Block -> immutable action -> World
  Authority evaluation -> durable or ephemeral result -> Physical
  Meaning/presentation intent -> Unity adapter`.
- Weapon interaction no longer relies on a controller-owned action ID or probe
  distance. Each item projects `equip_action`, `unequip_action`, and
  `interaction_range`; the matching equip or unequip Rule Block owns the
  relation change. Removing either block removes its route.
- Player ground locomotion uses world-avatar Triples for its action ID,
  movement speed, sprint speed, gravity, Physical Meaning, and animation
  intents. Unity sends transient intent and presents Authority approval. It
  does not restore local movement constants when data or a Rule Block is
  missing. The player jump runtime is currently retired as a complete contract.
- Autonomous targeting and chase require explicit `target_concept`,
  `targeting_profile`, `chase_profile`, `target_action`, `chase_action`,
  target/chase Rule Blocks, attack action, and `AuthorityKinematic` meaning.
  Each candidate is accepted only by previewing the immutable target action
  against its assigned target Rule, and movement starts only after the
  immutable chase action passes its assigned chase Rule. These two actions are
  explicitly evaluation-only: an accepted decision produces no durable Fact
  or revision. World Authority queries arbitrary compatible entities and no
  longer treats a Unity player component, monster prefab, template name, or a
  scheduler-owned faction/concept filter as target eligibility. The scheduler
  also resolves every assigned Rule ID from the immutable action definition;
  it contains no built-in list of monster Rule Block IDs.
- A projected entity with `Damageable` meaning receives the combat presenter
  dynamically. Removing that meaning disables the presenter. Targeting,
  death, hit, and loot presentation therefore remain portable to a log, rock,
  generated creature, or any other compatible visual.
- Attack damage and defeat are evaluated first. Loot availability is a
  separate declared post-Rule invocation with its own target binding, and
  collection has its own Rule Block/action. The post-Rule is conditionally
  optional so a non-fatal attack succeeds; removing it removes loot
  availability. Collection records target-owned durable provenance
  (`loot_status`, `loot_collected_by`) instead of overwriting one
  player-owned `collected_item` field.
- Existing semantic versions migrate by immutable definition version. A
  migration updates an existing binding only when the source binding is still
  present; it does not silently recreate a Rule Block the user removed.
- Harness evidence covers both enabled and removed paths for equip/unequip,
  ground locomotion, evaluated target/chase/attack, semantic presenter lifetime,
  and defeat/loot transitions.

## 56. Entity display-name and canonical term coverage

- Durable entity identity and stored instance names remain unchanged. The Unity
  presentation boundary hides only recognized system-generated instance
  suffixes: numeric placement ordinals and eight-character Authority GUID
  prefixes. A user-authored instance name is always displayed unchanged.
- Catalog definitions own the localized base name through a required
  `displayNameKey`. Duplicate system-named entities may use a presentation-only
  ordinal in lists; the ordinal is never written back to Authority or world
  Facts.
- Every canonical concept referenced by actor profiles, map-object templates,
  Rule Block presets, rule conditions/effects, or catalog semantic migrations
  must be registered in `OntologyTerms.csv` and have English and Korean labels.
  Project coverage validation fails when a referenced concept is missing or is
  registered with the wrong term kind.
- Filename/prefab-name inspection is an editor authoring aid only. It may
  propose blank display metadata but cannot infer gameplay meaning, overwrite
  authored names, or become a runtime name-based behavior branch.

## 57. Project-owned UI typography

- Project-owned game UI uses the dynamic TextMesh Pro asset generated from
  `Assets/UI/Fonts/Jua-Regular.ttf`. The TMP project default, TOV scenes, and
  project-owned ontology UI prefabs under `Assets/Prefabs/Ontology/UI` and
  `Assets/Data/Ontology/UI` use the same asset so runtime-created text and
  hierarchy-authored text remain consistent.
- Text under a Unity `Selectable` control (buttons, toggles, dropdowns, input
  fields, and their templates/placeholders) is enlarged by exactly 2 points
  when migrated to Jua. Non-interactive labels retain their authored point
  size, and layout, transforms, alignment, colors, hierarchy, and user edits
  are not changed.
- The typography migration is idempotent: the point increase occurs only when
  a text component changes from another font to Jua. Re-running the tool does
  not keep increasing sizes. Third-party UI assets are not modified.

## 58. Ontology data-panel localization contract

- The ontology data panel resolves its title, tabs, mode-sensitive add action,
  save/load actions, placeholders, row actions, physical choices, rule
  variables, relations, and semantic values through language-pack keys. Scene
  or prefab text is only an authoring fallback.
- Canonical ontology IDs remain English in authored Triples, Rule Blocks,
  Authority commands, and saves. `OntologyTerms.csv` maps every player-visible
  durable relation or semantic value to a display key; English and Korean
  labels never replace canonical stored data.
- Every registered relation used by the data panel has English and Korean fact
  templates. Missing term labels, fact templates, physical choice/result keys,
  or duplicate/ambiguous aliases fail the Unity localization regression suite.
- Known runtime-generated instance names use their catalog display key in UI,
  including Authority GUID/ordinal suffixes and editor `_Copy` suffixes.
  Canonical identity remains unchanged. User-authored instance names and free
  text are never stripped or translated.

## 59. Runtime lifecycle and world-entry convergence

- Autonomous runtime eligibility is projected, not inferred from a prefab or
  cached presenter. A scheduled actor and its target must have an active
  boolean `is_alive=true` Fact in addition to the complete action, Rule Block,
  and Physical Meaning contract. Losing eligibility evicts the actor's
  ephemeral motion record, so a defeated or deconfigured entity cannot keep
  moving as a visual ghost.
- An entity whose Physical Meaning assigns live position ownership to a
  runtime registry may be targeted only from a fresh runtime position. World
  Authority never falls back to its durable spawn/checkpoint while the live
  position is missing. Durable transforms remain valid for static targets
  whose meaning has no runtime position owner.
- World entry is one ordered transaction, not a collection of activation
  patches. The checkpoint controller restores durable data only. The
  `OntologyWorldEntryPresentationCoordinator` then keeps the active avatar
  hidden and input-gated, waits through fixed-step collision readiness, clears
  stale input and animation presentation, and revision-confirms any corrected
  grounded pose with World Authority. Only after those phases succeed may the
  session become `InWorld`. A missing dependency, timeout, or rejected
  confirmation fails entry without exposing gameplay.
- The generic runtime gate may disable dedicated world UI roots and runtime
  behaviours, but it may never disable a GameObject inside the local avatar
  presentation boundary. Avatar-attached helpers such as `OntologyActorToast`
  are gated as behaviours. Before the boundary opens, account entry also sends
  one transient zero-motion intent to validate the avatar's assigned
  locomotion action and Rule Block. The first visible idle/grounded frame
  therefore does not depend on the player's first movement input and creates no
  durable world event.
- Base locomotion animation is presentation state derived after the movement
  owner has updated for the frame. The animation adapter re-evaluates approved
  idle/locomotion intent in `LateUpdate`; it does not grant movement, persist
  per-frame animation Facts, or replace Authority approval.
- Only an accepted `execute_action` command may enter the command-based
  animation readiness queue. Checkpoint, placement, meaning-package, and other
  non-action commands do not repeatedly attempt to resolve an animation intent
  they cannot own.
- Removing a meaning-package-owned Rule Block retracts that binding and its
  RuleBound results without deleting sibling bindings. Shared package Facts
  remain until the final owned binding is removed; final removal retracts the
  package-owned canonical and typed Facts and restores only displaced authored
  baseline values. Independent lifecycle, account, or differently owned Facts
  remain by design.

## 60. Combat runtime availability and Rule-result lifetime

- Entering `InWorld` opens one explicit runtime boundary. Combat input,
  Authority targeting, avatar input, and runtime presentation are disabled
  before that boundary and re-resolve their Authority dependencies when it
  opens. An account or loading screen therefore cannot retain a stale combat
  subscription, and a successful world entry does not depend on component
  enable order.
- Zone simulation presence is not owned by SignalR. Unity renews an ephemeral
  authenticated HTTP Zone-session lease after player runtime activation and
  releases it on world/Zone/session exit. SignalR remains an optional revision
  notification channel. Losing a WebSocket cannot silently stop autonomous
  target, chase, or attack scheduling while the authenticated client is still
  in the Zone.
- Action references are relation-scoped canonical values. Relations such as
  `attack_action`, `target_action`, and `chase_action` preserve the exact
  immutable action ID instead of passing through the global display-value
  alias table. Single-cardinality relations are replaced atomically when a
  meaning package is applied, so a legacy alias and its canonical action cannot
  coexist and make action resolution ambiguous.
- Rule effects declare `RuleBound` or `DurableState` result lifetime.
  `RuleBound` results, such as an equipment relation owned by its assigned
  block, are retracted when that Rule Block is removed. Accepted damage, death,
  respawn, and loot transitions are `DurableState`: removing their Rule Block
  prevents future transitions but never rewinds history already committed by
  World Authority.
- The lifetime is stored as provenance on each action-produced world Fact.
  Publishing a newer immutable Rule definition promotes legacy active results
  only when the new definition declares the same result predicate as
  `DurableState`; Authority contains no health, death, loot, weapon, monster,
  prefab, or object-name migration list.
- Existing player avatars migrate to semantic contract version 4. Missing
  `current_health` is restored from the avatar's unique authored
  `maximum_health` and `is_alive` state, never a Unity numeric constant.
  Existing respawn bindings move to the version published in
  `WorldAuthoritySettings`; a binding intentionally removed by the user is not
  recreated.
- Adding an optional Rule schema field must not invalidate an already
  published immutable version when the field carries only its legacy default.
  Canonical Rule serialization therefore omits default `RuleBound` lifetime,
  while an explicit non-default `DurableState` remains immutable content.
  This is a wire-compatibility rule, not permission to alter an authored Rule
  at the same version.

## 61. Locomotion approval scope and continuous grounding

- A player locomotion approval fingerprint contains only the authored
  ground-locomotion facts, assigned Rule Block, immutable move action,
  world, and Zone that participate in Authority evaluation. Combat,
  equipment, profile, respawn, animation, and other unrelated avatar changes
  do not revoke an accepted movement lease.
- When a relevant contract really changes, Unity fails closed for new
  locomotion and automatically requests a zero-motion Authority
  revalidation. Revalidation does not wait for the next key press and creates
  no durable Fact.
- Authored gravity is continuous Physical Meaning. It continues settling the
  already visible `CharacterController` while a transient movement lease is
  being revalidated and drives falling without creating an upward impulse.
- The current production player contract has no jump input, jump transport,
  `jump_avatar` action, `JumpPlayerFromIntent` Rule Block, local jump impulse,
  or `JumpStart` selection route. Losing ground contact remains an airborne
  presentation observation.
- World entry resolves the projected `is_alive` state before requesting its
  locomotion lease. A dead saved avatar executes the authored `respawn_action`
  through its assigned `RespawnPlayerOnDeath` Rule Block, reloads and verifies
  the Authority projection, and only then starts the movement handshake.
  Unity never writes life or health locally to bypass entry.

## 62. Authority-approved hybrid player jump and impact ownership

- Section 61 recorded the temporary retirement of jump while the former
  double-motion implementation was removed. The production contract now
  restores jump as a new versioned ontology slice rather than re-enabling that
  local shortcut.
- The durable avatar authors `jump_action`, `jump_takeoff_speed`,
  `gravity_acceleration`, `ground_stick_velocity`, and
  `impact_response_profile`, and is assigned `JumpPlayerFromIntent`.
  `jump_avatar` is an immutable evaluation-only action that invokes that Rule
  Block. Removing the action Triple or Rule Block removes the route.
- The restored hybrid contract is published as `JumpPlayerFromIntent` version
  2 and `jump_avatar` version 2 in development package 3.8.1. Version 1 remains
  immutable historical content. Player semantic contract version 6 retracts
  an active version-1 jump binding and assigns version 2 before world entry;
  it never overwrites the published version-1 definition.
- Space produces only a transient jump request and the current grounded
  collision observation. World Authority rejects the action unless the full
  authored contract is present and `requiresGroundedObservation` is satisfied.
  Grounded samples and vertical velocity never become durable world Facts.
- After approval, Unity applies the authored takeoff speed and gravity to its
  local collision presentation. Horizontal motion, Authority correction,
  gravity, jump, and controller-mode impulse are combined into exactly one
  `CharacterController.Move` call per frame. The returned
  `CollisionFlags` resolves ceiling and landing state without a second move.
- `impact_response_profile` selects a reusable response adapter. A
  `ControllerImpulse` joins the same controller move. A
  `TemporaryRigidbodyReaction` may run only with an independent solid collider;
  it suspends CharacterController ownership, owns the transform exclusively
  during the reaction, and explicitly restores controller ownership after
  settling. If that safe shape is absent, the adapter fails back to the single
  controller owner rather than running two solvers.
- Jump approval is Authority-evaluated and ephemeral. Until the Authority owns
  a terrain collision representation, the vertical arc remains the local
  Unity collision presentation; it is not written to the durable avatar spawn
  transform or asserted as server-authoritative remote vertical simulation.
- One accepted jump increments one ephemeral presentation occurrence. The
  animation resolver consumes that occurrence through `JumpStart`, then uses
  vertical collision observations for `Airborne`/`Fall`, and enters `Landing`
  once after support returns. `JumpStart` normally finishes with its
  manifest-authored playback segment, but the observed apex immediately ends
  the takeoff phase if the clip outlasts physical ascent. `Landing` finishes
  with its authored segment; no fixed timer owns either transition.
- Direct player motion remains owned by CharacterController, so every jump
  lifecycle manifest entry must explicitly use disabled root motion. The
  current third-party `Jump_End` source remains untouched; its project manifest
  selects only the normalized 0.85-1.00 settling segment, excluding the source
  clip's rising phase that previously looked like a second jump.

## 63. Local character motion and collision-role ownership

- `physical_profile LocalCharacterController` selects one Unity motion driver.
  `OntologyCharacterMotionCoordinator` is the only normal locomotion component
  that invokes `CharacterController.Move`; input, gravity, swimming and
  approved impact adapters contribute displacement to that owner.
- `CharacterController.stepOffset` remains zero. Explicit step solving uses the
  current-frame support probe and accepts only a `WalkableSupport` collision
  role. `ActorBody`, `DynamicProp`, interaction triggers, water volumes and
  other actors are obstacles and can never lift the player as a stair.
- World-entry and respawn grounding place the capsule at its real radius plus
  skin width, then perform one coordinator-owned contact sample while input is
  gated. The first user move therefore does not depenetrate a capsule that was
  restored partly inside the floor.
- A Physical Meaning profile also declares its exclusive `motionDriver` and
  `collisionRole`. Removing the profile disables that driver lease and its
  adapters; it does not leave component-name or player-prefab fallbacks.
- Authority continues to evaluate the locomotion/jump action and assigned Rule
  Block. Unity collision observations and resolved poses are ephemeral.
  `WorldPlayerMotionSimulationScheduler` no longer integrates a second
  collisionless Transform. The client publishes sequence-checked,
  Zone-bounded resolved poses for remote presentation until shared collision
  simulation is introduced.

## 64. Drowning recovery movement ownership

- An inferred `movement_mode Drowning` relation grants the drowning
  presentation temporary exclusive ownership before normal input, jump,
  gravity, UI capture, and `CharacterController` locomotion are evaluated.
- Recovery may intentionally disable `CharacterController` while sinking.
  Its state machine is therefore updated from the player input lifecycle
  itself, not from inside a controller-enabled locomotion branch. Disabling
  the controller cannot stop the recovery update that must re-enable it.
- While recovery owns movement, transient vertical velocity, controller
  impulse, click navigation and locomotion animation input are cleared. They
  cannot launch the avatar upward or resume stale travel after the confirmed
  checkpoint is restored.
- Removing the `Drowning` relation immediately releases recovery ownership.
  Unity then restores the controller and resumes the normal ontology-approved
  locomotion path; no water-name, mesh-name, or hidden local drowning fallback
  exists.

## 65. Collision-resolved planar locomotion

- The support probe is ephemeral collision evidence only. Its sampled triangle
  normal does not rewrite an authored horizontal input vector into a vertical
  displacement.
- The single motion coordinator passes world-planar locomotion plus authored
  vertical velocity to one `CharacterController.Move` call. CharacterController
  resolves the slope and collision once; Unity code does not pre-resolve the
  same surface and then resolve it again.
- Explicit role-aware step solving remains separate from jump and gravity, but
  a face or triangle on the collider that currently supports the avatar is
  continuous terrain, not a new step. Only a distinct `WalkableSupport`
  collider may provide an explicit step top.
- Opt-in development diagnostics report an unrequested controller rise or an
  external Transform write as `[PlayerMotionTrace]`. They are disabled during
  normal play. When explicitly enabled, the logs remain transient presentation
  evidence and never create a Fact, rule result, revision, or network message.

## 66. Jump presentation lifecycle ownership

- World Authority still evaluates the authored jump action and assigned Rule
  Block. Its accepted `JumpStart` presentation intent identifies the canonical
  jump occurrence; it does not own a second clip-duration state machine.
- For an avatar whose manifest-driven local motion resolver is active,
  animation entries explicitly authored with
  `presentationOwner MotionStateResolver` route to that collision-backed
  lifecycle. The shipped `JumpStart`, `Airborne`, `Fall`, and `Landing`
  entries use this metadata; no intent-name list in Unity grants ownership.
- Canonical direct-motion `Fall` means airborne descent. A ground-collapse or
  damage-reaction clip may use a separate reaction intent, but must not claim
  `Fall` merely because its source animation file is named "Fall". The shipped
  player profile continues its in-place airborne pose while descending and
  switches to the authored landing segment only when support returns.
- Generic Authority transient playback remains available for one-shot action
  presentation such as attack and equipment actions. It must not capture a
  direct motion intent and prevent the observed apex or landing from changing
  the selected clip.
- Removing the jump Rule Block still removes jump approval. This ownership split
  adds no local permission fallback and writes no movement observation or
  animation phase as a durable Fact.

## 67. Physical-meaning and animation-metadata hardening

- `OntologyMotionDriverAdapter` is fail-closed. A missing, disabled, or
  mismatched adapter derived from Physical Meaning is never treated as
  permission to move a Transform. Removing Physical Meaning therefore removes
  its presentation driver without a component-presence fallback.
- `maximumStepHeight` belongs to `OntologyPhysicalProfile`. The local character
  motion coordinator receives that value only while the selected profile owns
  `LocalCharacterController`; profile removal clears the derived tuning.
- Animation lifecycle ownership is authored in
  `AnimationContentManifest` and synchronized into `AnimationDatabase`.
  Conflicting owners for the same canonical intent fail validation, and
  motion-state-owned entries must disable root motion.
- Canonical animation IDs may declare explicit compatibility aliases during a
  migration. The collapse reaction is now `Anim_Collapse_Reaction`; existing
  durable `Anim_Fall` references resolve only as a legacy alias and are never
  newly authored.
- The movement-state resolver consumes only the current observation snapshot.
  Its removed `deltaTime` argument never influenced a transition. The content
  pipeline no longer infers intents or looping behavior from clip properties;
  it preserves the manifest as the sole presentation authoring source.

## 68. Explicit collision layers and legacy-motion retirement

- The project-owned TOV runtime no longer contains or depends on the third-party
  `CharacterMover` and `MovePlayerInput` components or the transitional
  `OntologyPlayerController`. `OntologyCharacterMotionCoordinator` remains the
  one local `CharacterController.Move` owner selected by
  `LocalCharacterController` Physical Meaning.
- `OntologyPhysicalProfileDatabase` authors the presentation mapping from every
  semantic `OntologyCollisionRole` to a project-owned Unity Physics Layer and
  its collision matrix. The canonical role remains ontology data; a Unity layer
  is only an adapter detail and must never become a rule identifier.
- The current project layers are `WorldStatic`, `ActorBody`, `DynamicProp`,
  `InteractionTrigger`, and `WaterVolume`. Semantic synchronization applies the
  mapped layer to collider-bearing objects and restores the original layer when
  the Physical Meaning is removed.
- A static Unity collider is no longer implicitly `WalkableSupport`. Terrain
  and other support surfaces must carry an explicit collision-role adapter.
  The character support probe queries only the authored `WalkableSupport`
  layer, while actor bodies cannot become stairs or support surfaces.
- This milestone hardened the client collision-prediction boundary before the
  fixed-tick integration described in section 70. Unity still owns local
  prediction presentation, while World Authority now owns the shared motion
  result.

## 69. Authored server collision-proxy contract

- A world entity may author `collision_role`, `collision_proxy_shape`, bounded
  dimensions, and optional center offsets. Together with the durable entity
  Transform these facts form the only input to a lightweight World Authority
  collision proxy. Prefab names, mesh bounds, scene object names, and Unity
  Collider components are never server collision meaning.
- The first supported proxy shapes are canonical `Capsule` and `Box`.
  `Capsule` requires positive `collision_radius` and `collision_height`, with
  height at least twice the radius. `Box` requires positive
  `collision_size_x`, `collision_size_y`, and `collision_size_z`. Invalid,
  incomplete, non-finite, or excessively large data fails closed.
- Player semantic contract version 8 authors
  `collision_role ActorBody`, `collision_proxy_shape Capsule`, radius `0.45`,
  height `2`, and center offset `(0, 1, 0)`. These values match the project
  CharacterController contract and are migrated through the existing
  revisioned authored-Fact path. Removing them after version 8 is preserved.
- World Authority can now project generic proxy configurations for any entity,
  so a user-authored object can later become support, an actor body, a dynamic
  prop, a trigger, or water without a template-name branch. The same policy
  already validates the whole proxy against Zone boundaries rather than only
  validating its center.
- This milestone established trusted server geometry input. Section 70 now uses
  that input for fixed-tick Zone, obstacle, gravity, and jump integration.
  Authored support elevation and smooth local reconciliation remain later
  movement-authority phases.

## 70. Fixed-tick World Authority player motion

- World Authority now advances active player avatars every 50 ms from ephemeral
  canonical locomotion intent. Direction is normalized and requested speed is
  clamped by the authored `movement_speed`; a client-reported coordinate is not
  an input to this integration.
- An already evaluated runtime action is recorded as a short-lived generic
  action occurrence. The scheduler treats it as jump input only when its
  canonical action ID equals the avatar's authored `jump_action`, and then uses
  the authored `jump_takeoff_speed`, `gravity_acceleration`, and
  `ground_stick_velocity`. There is no action-name or animation-name branch.
- The scheduler keeps the entire authored player Capsule inside the Zone and
  resolves planar movement against authored `ActorBody` and `DynamicProp`
  Capsule/Box proxies. Missing movement tuning or the player collision proxy
  disables motion instead of restoring a visual or component fallback.
- `/resolved-pose` now accepts only a finite, owned, in-sequence client
  observation associated with the current movement intent. It stores that
  observation separately for diagnostics and future reconciliation; it cannot
  mutate the authoritative motion registry.
- The former durable entry/checkpoint-Y ground reference has been removed.
  Section 71 replaces it with authored `WalkableSupport` elevation and bounded
  local planar reconciliation.

## 71. Authored support elevation and planar reconciliation

- A project-owned starter world now provisions one deterministic durable
  support entity during owner world entry. It authors `collision_role
  WalkableSupport`, `collision_proxy_shape Box`, bounded dimensions, a durable
  transform, and `physical_profile StaticAnchored`. The configuration is
  editable in `WorldAuthoritySettings`; the server does not inspect the Unity
  plane, mesh, prefab, object name, or Collider to derive it.
- Passive support geometry does not initiate an action, so a new Rule Block is
  not applicable to that entity. Locomotion and jump still require their
  existing immutable actions and assigned Rule Blocks. Retiring the support or
  removing its support/proxy meaning removes server grounding; the deterministic
  ID is not used to recreate an intentionally removed entity.
- Player semantic contract version 9 adds authored
  `maximum_step_height 0.3` and `ground_clearance 0.03`. World Authority selects
  the highest valid authored Box below the complete player footprint, applies
  the authored clearance to root height, permits only the authored step range,
  and begins gravity when support is absent. Durable checkpoint Y is never a
  substitute for missing support.
- Authority motion snapshots expose their supporting entity ID. Unity polls
  only the owned local avatar state, rejects stale or invalid snapshots, and
  extrapolates planar position for at most a bounded presentation window.
  The resulting X/Z error is capped and queued into
  `OntologyCharacterMotionCoordinator`; it is consumed by the next and only
  `CharacterController.Move` call. No direct Transform write or second Move is
  permitted.
- Vertical client reconciliation remains intentionally local-collision-owned in
  this first support slice. Pulling Y toward the server before slope,
  multi-level, and moving-support snapshot contracts exist could reintroduce
  the landing pop that this movement architecture removed. The current support
  proxy is a flat Box; terrain height fields and moving supports are later
  authored proxy types, not mesh-name exceptions.

## 72. Contact-owned landing and authored air presentation

- A support-probe hit means that an authored support surface is nearby; it does
  not mean that the CharacterController has landed. Ground contact now requires
  both an authored `WalkableSupport` observation and the controller's current
  grounded/`Below` collision result. Support proximity alone cannot approve a
  jump, end an airborne state, or start Landing.
- One approved jump occurrence still owns the ephemeral presentation lifecycle.
  The movement-state resolver selects `JumpStart`, `Airborne`, `Fall`, and
  `Landing` from vertical motion plus collision contact and never writes those
  phases as durable Facts.
- `Airborne` and `Fall` now resolve to separate canonical Manifest entries.
  The current first slice reuses different authored playback segments from the
  same licensed source clip, but the segment boundaries, looping, transition,
  root-motion mode, and presentation owner are Manifest data rather than code
  constants or clip-name branches.
- `Landing` starts only after physical contact and uses a Manifest-authored
  blend. Idle, locomotion, or equipment idle is selected only after the landing
  segment completes. Removing one required motion-state entry fails animation
  content validation; Unity does not replace it with an object-name,
  animation-name, timer, or proximity fallback.

## 73. Actor-profile repertoire survives runtime world rebuilds

- The animation Manifest remains the authoring source and synchronizes the
  runtime Database plus assigned ActorProfiles. An ActorProfile synchronizer
  projects the current repertoire as `ActorProfile`-origin `has_animation`
  contributions; these are runtime profile projections, not durable authored
  world Facts.
- `OntologyWorldBootstrap` may replace its runtime world during scene
  composition, restore, or entry. Profile synchronizers subscribe to
  `WorldRebuilt` and republish the current profile after replacement, so a
  newly registered canonical animation is available to existing characters
  without an animation-ID or character-name fallback.
- Repertoire removal remains authoritative. Synchronization first retracts the
  previous ActorProfile contribution, so removing a Manifest/profile assignment
  removes the matching runtime animation route instead of retaining a stale
  clip.
