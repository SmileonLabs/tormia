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

## Related documents

- [Korean project context](PROJECT_CONTEXT.ko.md)
- [Project README](../README.md)
- [Local platform services](../infrastructure/README.md)
- [Ontology language-pack contract](../Assets/Resources/Ontology/Localization/README.md)
- [Actor animation handoff](../ACTOR_ANIMATION_HANDOFF.md)
