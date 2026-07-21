# Tormia local platform services

This folder is the development equivalent of the production data platform. It deliberately runs only stateful infrastructure:

- PostgreSQL: persistent worlds, authoring data, permissions, revisions, commands, and events.
- Redis: disposable session, cache, and server-to-server coordination data.
- World Authority: the only API allowed to turn a user command into a durable
  world change. Unity must never connect to PostgreSQL or Redis directly.

The authoritative Unity Headless server is added later. It will connect to these services; it must not be embedded in the database image.

## Start locally

From the repository root in PowerShell:

```powershell
Copy-Item infrastructure/.env.example infrastructure/.env
# Replace both passwords in infrastructure/.env before continuing.
docker compose --env-file infrastructure/.env -f infrastructure/docker-compose.yml up -d postgres redis
docker compose --env-file infrastructure/.env -f infrastructure/docker-compose.yml run --rm migrate
docker compose --env-file infrastructure/.env -f infrastructure/docker-compose.yml up -d --build world-authority
```

## Unity local-authority connection

The Unity project connects only to the authority HTTP endpoint; it never receives
a PostgreSQL or Redis connection string.

1. In Unity, run `Tools > Ontology > Networking > Setup Local Authority Connection` once.
2. Edit `Assets/Data/Ontology/Networking/WorldAuthoritySettings.asset`.
   Keep `http://127.0.0.1:5272` for the local Docker stack.
3. Enter Play mode. On `OntologyWorldSample`, use the `Ontology World Authority
   Client` component's **Connect to Local Authority** context action.
4. To publish pre-existing placed objects, first run `Tools > Ontology > Networking
   > Assign Stable Entity IDs to Placed Objects`, save the scene, then use the
   bridge component's **Publish Existing Placed Objects to Authority** action.

`Setup Local Authority Connection` also adds **Ontology World Authority Realtime
Client** to the same bootstrap object. It is enabled by default in the settings
asset but becomes active only after the authority client has joined a world.
It listens for committed `worldRevision` notifications and then asks the existing
HTTP authority client to reload the projection. If its socket is unavailable,
the client automatically returns to the normal short HTTP polling interval.

`realtimeZoneKey` on `WorldAuthoritySettings.asset` remains a useful fixed
development override. At runtime, `OntologyWorldZoneStreamer` reads the durable
zone directory and selects the Zone containing the player (or the nearest Zone
within its activation distance). The selected key scopes both the SignalR group
and the projection request (`GET /v1/worlds/{worldId}?zoneKey=...`). Outside every
authored Zone, the client deliberately falls back to the whole-world projection.
A zone projection contains only that zone's entities and their authored facts/rule
bindings. Entity references that cross a zone boundary remain identifiers, rather
than causing the other zone to be loaded implicitly.

For a second local client, duplicate the settings asset, change its development
subject and display name, then paste the first client's authority world GUID into
`sharedWorldId`. This is intentionally an explicit development workflow. The next
slice changes editor actions into server-confirmed commands and applies remote
projections back to the scene.

Local-only endpoints:

```text
PostgreSQL: localhost:54329
Redis:      localhost:56379
World API:  http://localhost:5272
```

The ports bind to `127.0.0.1`, so they are not exposed to the local network.

## Local authority API

This is an intentionally small, server-authoritative vertical slice. It supports
creating a development user and world, then applying the following durable
authoring commands:

```text
place_entity
move_entity
set_authored_fact
retract_authored_fact
add_rule_block
remove_rule_block
register_player_avatar
```

For local development only, create a user via `POST /v1/dev/users`, then send
the returned UUID as `X-Tormia-User-Id`. Production replaces this temporary
header with verified JWT/OIDC identity before the API reaches command handling.

Every accepted command is serialized per world, checks `expectedRevision`,
writes one immutable `world_events` entry, and advances the world revision in
the same PostgreSQL transaction. Replaying the same `commandId` is idempotent.

## Development account → character → world entry

The local stack now has a deliberately small account foundation. `app_users`
stores the development identity, while `player_characters` stores account-owned
character profiles (name, template and equipped-part identifiers). These are
not world Facts: a profile may be reused in different worlds without a world
rule or edit changing the account save.

`GET /v1/account` returns only the authenticated account's character profiles
and accessible worlds. `POST /v1/account/characters` creates another saved
profile. `POST /v1/worlds/{worldId}/entry` binds the selected profile to the
user's already registered world avatar. This link is ownership/session metadata
and does not advance a world revision or create a Fact.

In Unity, `OntologyWorldAuthorityClient` loads this dashboard after the local
development identity is connected and remembers the selected character/world in
PlayerPrefs. `OntologyWorldAuthorityAccountEntryFlow` provides inspector context
actions for the development sequence: **Connect Development Account** →
**Refresh Account, Characters and Worlds** → **Enter Selected Character in
Current World**. The local player must already be published to the chosen world
so its stable entity ID can be registered before entry.

This is not production authentication. Email/social sign-in later supplies a
verified user subject/token to the same account boundary; Unity never stores a
password.

## Player intent foundation

`register_player_avatar` is a durable, revisioned command that assigns one
world entity to the authenticated player in that world. It is authority metadata
for ownership validation, not an ontology Fact or rule.

`POST /v1/worlds/{worldId}/runtime/intents` accepts the latest transient
movement sample (`avatarEntityId`, `zoneKey`, `sequence`, `moveX`, `moveZ`,
`jump`) only from that avatar's owner. Samples have a short Redis lease, reject
stale sequence numbers, and do **not** advance a world revision or create a
world event. The server motion worker consumes this verified input and produces
an ephemeral authority position without writing durable transforms. `GET
/health/intents` reports its backend.

### Unity input sender

`OntologyWorldAuthorityPlayerIntentSender` is the optional Unity transport
component for this phase. It observes the existing Input System controller and
sends world-space movement samples only after its player entity has been
published and registered with `register_player_avatar`. It is intentionally
disabled by default and does not change the current local CharacterController,
swimming, collision, or animation behavior.

### Unity horizontal reconciliation

`OntologyWorldAuthorityPlayerMotionReconciler` polls the authority runtime
state and softly reconciles **X/Z only** after the sender has registered its
avatar. It is also disabled by default. This lets local controls remain
responsive while the server position becomes the horizontal reference. Y,
terrain collision, gravity, water response, mounting, and ontology movement
adapters remain local until the authority has an equivalent collision/world
representation; the reconciler deliberately does not guess or overwrite them.

## Server kinematic motion runtime

The authority now runs a separate 5 Hz player-motion worker. For an avatar in a
non-dormant Zone, it combines three intentionally distinct inputs:

1. the durable entity transform supplies the spawn position;
2. the avatar's authored `movement_speed` **number** Fact supplies speed;
3. the latest owner-authenticated controller intent supplies a normalized
   world-space X/Z direction.

The result is an ephemeral authoritative motion state, available at
`GET /v1/worlds/{worldId}/runtime/avatars/{avatarEntityId}`. It clamps movement
to the durable Zone boundary, expires naturally with Redis, and never rewrites
the authored entity transform or creates a Fact/event per tick. The current
engine is deliberately reported as `zone_bounded_kinematic`: terrain collision,
gravity, water response, and ontology movement-mode adapters still require a
server-side collision representation before they can be authoritative. A
separate Redis execution lease (`player-motion`) prevents two server replicas
from advancing the same Zone's avatars simultaneously.

## Remote avatar presentation

`GET /v1/worlds/{worldId}/runtime/zones/{zoneKey}/avatars` returns only the
ephemeral motion state of registered avatars in the requested accessible Zone.
It intentionally contains no owner metadata and never creates durable world
data. A registered avatar is listed only while its owner's authenticated
SignalR session lease is active in that Zone, so closing a client removes its
remote presentation immediately instead of waiting for the motion-cache TTL.
Unity's `OntologyWorldAuthorityRemoteAvatarPresenter` consumes this list
as a read-only presentation layer: remote objects do not run player input,
physics, swimming, or ontology adapters. It is disabled by default and can use
a configured remote-avatar prefab. If no prefab is assigned it uses a
non-colliding capsule fallback for local integration testing.

When the scoped authority projection changes, the same presenter reads the
durable `equipped_part` Facts for each remote avatar and applies them through
the local `CharacterPartDatabase` to its visual-only prefab. Its movement
animation is driven by the authority motion status and a configured source
Animator controller/Avatar. This preserves the boundary: character appearance
comes from durable ontology facts, while position and moving/idle status come
from ephemeral runtime state.

When SignalR is connected, the motion worker emits a small `zoneRuntimeChanged`
**hint** only after a visible avatar state changes. Unity then reads this same
HTTP snapshot immediately; a two-second safety refresh remains in case a socket
notification is missed. When realtime is unavailable it falls back to the
configured HTTP interval. The hint does not carry positions, does not write
scene transforms, and is not a second source of simulation truth.

## Real-time world-zone sessions

The authority also exposes a SignalR endpoint at `/hubs/world-zone`. This is a
**notification channel**, not another rule or authoring path:

- a client connects with `worldId`, optional `zoneKey`, and its authenticated
  identity (the temporary local header is `X-Tormia-User-Id`);
- it receives `worldRevision` only after PostgreSQL has committed a command;
- it then reloads the projection through the normal authority API;
- Redis distributes that notification between authority instances, while
  PostgreSQL remains the single durable truth.

This split is intentional. It prevents a socket message, a cache, or a Unity
client from becoming an alternate source of ontology facts or rules. The Unity
realtime component processes socket data only as a revision hint and then reloads
the server projection. Zone groups are the unit that will later let us scale
active simulation and realtime fan-out independently without broadcasting every
editor change to every player.

`GET /v1/worlds/{worldId}/zones` returns the accessible zone directory and
`GET /health/realtime` reports the selected backplane mode.

## Zone session leases

When a Unity client joins `/hubs/world-zone`, the authority records a short-lived
Zone session lease in Redis. The client renews it through a SignalR heartbeat;
disconnecting removes it, while the 90-second lease cleans up a crashed client.
This is operational presence only, not an ontology Fact and not a gameplay rule.

`GET /v1/worlds/{worldId}/zones/{zoneKey}/session` returns the current authorized
connection/user count and scheduling eligibility: `active`, `idle`, `reduced`, or
`dormant`. `GET /health/sessions` confirms whether the shared Redis registry is
available. A later simulation worker will consume this eligibility signal; it is
not implemented as a fake Unity-side simulation loop.

## Zone scheduler and execution lease

`WorldZoneSimulationScheduler` now turns the durable Zone mode and shared session
count into a runtime snapshot every second. `active` plans a 1-second tick,
`reduced` plans a 5-second tick, while `idle` and `dormant` plan no tick. For an
active/reduced Zone it obtains a short Redis execution lease, so only one authority
replica owns that Zone's future simulation tick at a time.

The runtime endpoint is `GET /v1/worlds/{worldId}/zones/{zoneKey}/runtime`. It
shows the owner, connection counts, chosen mode, planned interval, and the
headless inference result for the latest owned tick. The evaluator shares the
same pure `Assets/Scripts/Ontology/Core` rule semantics through the
`Tormia.Ontology.Runtime` project, but deliberately accepts only `AddFact`
inference effects. Its results are an ephemeral runtime projection; it never
writes inferred data into durable `world_facts` and it never executes Unity
physics or animation adapters. `engineStatus` reports `inference_only`,
`no_inference_rule_bindings`, or `missing_published_rule_catalog` so a missing
server-published rule is visible rather than silently simulated on the client.
`GET /health/scheduler` verifies this orchestration layer.

## Rule catalog publishing

Rule definitions are immutable server content. A world Rule Block now accepts
only a published `ruleId + ruleVersion`; this prevents an edited local asset from
silently changing a rule already used by a shared world.

1. In Unity open **Tormia → Ontology → Publish Rule Catalog**.
2. Select the Rule Database and World Authority Settings, then publish a package
   such as `tormia_local` to the local authority.
3. For a semantic rule change, raise that rule's **Catalog Version** in the Rule
   Database before publishing. Existing bindings retain their old version; new
   bindings use the published current version.
4. Use **Refresh Server Status** before placing a Rule Block. The window marks
   each local `ruleId + Catalog Version` as published or not published.

The publisher uses the configured local development identity only for local
Docker use. The API itself is package-authorized (`owner` or `editor`) through
`content_packages` and `content_package_members`; a production identity provider
replaces the development-user bootstrap.

Zones are authored with the versioned `define_zone` command. Its payload is
`zoneKey`, `minX`, `minZ`, `maxX`, `maxZ`, and `simulationMode`
(`active`, `reduced`, or `dormant`). The authority validates the boundary and
records the command/event before publishing its revision hint. Zone definition is
therefore world data, not a hidden Unity scene setting.

### Unity Zone authoring flow

1. Under the scene's `World Zones` object, create an empty child such as
   `Village Zone`.
2. Add **Ontology World Zone Volume**. It supplies a trigger-only Box Collider;
   size and position it over the desired X/Z area, then set a unique `Zone Key`
   and its simulation mode.
3. Enter Play mode and select `OntologyWorldSample`. On **Ontology World Zone
   Publisher**, use the component menu **Publish Scene Zone Volumes to
   Authority**.

The collider is never a movement or physics surface. It is only the editable
scene representation of the server-owned Zone boundary.

## Migrations

Add immutable, ordered `.sql` files under `postgres/migrations`.

- Never edit a migration that another environment may already have applied.
- Add a new migration for every schema change.
- Run the `migrate` job once per deployment, before starting new game-server instances.
- A migration contains transactional DDL where PostgreSQL supports it.

## Data policy

Only durable world information belongs in PostgreSQL:

- world definition, entity placement, authored facts, rule bindings, commands, events, snapshots, and permissions;
- versioned content definitions such as object templates, rules, physical profiles, attachment profiles, and localization.

Do **not** persist per-tick observations or derived facts such as water occupancy, Floating, or Swimming. They live in the authoritative server's memory and are recomputed from durable facts and current observations.

## Production mapping

The same compose contract maps to production without changing the schema:

```text
Local Docker                     Production
------------                     ----------
Postgres container        ->    managed PostgreSQL / HA PostgreSQL
Redis container           ->    managed Redis
named volumes             ->    managed disks + backups
compose migrate job       ->    CI/CD migration job
local object files        ->    S3-compatible object storage + CDN
```

Credentials are supplied by a secret manager in production, never committed as `.env` files.
