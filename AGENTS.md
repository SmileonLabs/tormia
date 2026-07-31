# TOV Development Harness / 개발 하네스

This file is the operating agreement for people and AI agents working in this
repository. Read it together with `docs/PROJECT_CONTEXT.md` and
`docs/PROJECT_CONTEXT.ko.md` before making a system-level change.

## 1. Product and ontology rules / 제품·온톨로지 규칙

- Classify every change first: account profile, durable world data, runtime
  observation, inferred state, or Unity presentation.
- Do not add mesh-name, prefab-name, or object-name gameplay exceptions when a
  triple, profile, rule, rule block, or adapter contract can express the same
  meaning.
- A Unity adapter may present an ontology result, but it must not silently own
  the gameplay rule that caused the result.
- Account-owned character data is not a world Fact. World facts are not a place
  to store login/profile preferences.
- Rule-block removal must remove the matching behavior. Do not leave a hidden
  fallback implementation behind.
- Removing one meaning-package-owned Rule Block must retract only that binding
  and its `RuleBound` results while sibling bindings remain. The final owned
  binding closes the package and retracts its owned canonical and typed Triples
  and profile declarations; independent contributions must survive.
- Gameplay behavior must be traceable through the complete ontology contract:
  authored Triple data identifies the capability/profile, an assigned Rule
  Block enables the behavior, and Physical Meaning or a presentation adapter
  expresses the evaluated result. Unity input or presentation code must not
  create the same behavior when the required Rule Block is absent.
- When CharacterController owns direct player motion, jump animation content
  must explicitly disable root motion. `JumpStart`, `Airborne`, `Fall`, and
  `Landing` advance from one approved ephemeral jump occurrence and
  manifest-authored playback segments; do not use a fixed landing timer or a
  clip/name exception that can create a second visual apex.
- Runtime actor/target eligibility must come from the current projection,
  including active lifecycle state. Losing eligibility must evict ephemeral
  runtime state; a live-position owner must never fall back to a durable spawn
  transform for targeting.
- World entry completes only after local grounding/collision readiness and any
  corrected checkpoint pose is confirmed by World Authority. The first input
  frame must not be used as an implicit grounding repair.
- Every new reusable gameplay behavior must be authored as a Rule Definition
  and registered as a bindable Rule Block before an input, Authority command,
  or Unity adapter may invoke it. Input and transport code may publish a
  canonical intent or observation; they must not own the final gameplay
  relation or state transition.
- Merely checking `has_rule_block` inside an Authority action is not equivalent
  to executing that Rule Block. An action definition must not duplicate the
  block's result as a hidden direct effect.
- The harness evidence for a new behavior must prove all four boundaries:
  the Rule Block is registered and assignable, input emits intent only, the
  assigned block produces the result for any compatible object, and removing
  the block prevents or retracts the result.
- Use this canonical gameplay production line unless a stage is explicitly
  marked not applicable:
  `trigger -> canonical intent/observation -> authored Triples + assigned Rule
  Block -> rule evaluation -> origin-classified result -> Authority
  persistence/projection when durable or shared -> Meaning/Profile -> Unity
  adapter -> player experience`.
- Physical Meaning is one optional result branch, not a mandatory stage for
  dialogue, quest, UI, audio, or animation-only behavior. Every active branch
  still requires an evaluated result; an adapter must never infer permission
  from the trigger, object name, or visual asset.
- New gameplay harness scenarios must record an owner for every production-line
  stage and explain why any omitted Authority, physical, or presentation stage
  is not applicable.
- Every new or migrated weapon must extend the canonical
  `weapon-ontology-production` harness contract. Its production evidence must
  cover, in order: project-owned resource registration, authored capability
  and tuning Triples, assigned equipment/swing/attack Rule Blocks, immutable
  actions that invoke those blocks, Physical Meaning plus attachment/grip
  profiles, validated animation/VFX manifest entries, Authority evaluation and
  persistence, Unity presentation only, and the Rule-Block-removed path.
- Every new or migrated monster must extend the canonical
  `monster-ontology-production` harness contract. Its production evidence must
  cover, in order: project-owned visual/catalog registration, authored actor
  concepts and behavior Triples, an assigned autonomous Rule Block, an
  immutable action invoking that block, `AuthorityKinematic` Physical Meaning,
  validated animation manifest/profile entries, Authority simulation and
  durable combat results, Unity interpolation/presentation only, and the
  contract-removed path.
- Every new or migrated player capability must extend the canonical
  `player-ontology-production` harness contract. Its production evidence must
  cover, in order: account-profile versus world-entity ownership, authored
  player concepts/capabilities/tuning/action Triples, assigned locomotion,
  combat, equipment, death, and respawn Rule Blocks as applicable, immutable
  actions invoking those blocks, Physical Meaning, validated animation
  manifest/profile content, Authority evaluation and durable/shared results,
  Unity input/presentation only, and the contract-removed path.
- A prefab, mesh, animation, input binding, MonoBehaviour, Authority endpoint,
  or catalog entry by itself is never a completed player, weapon, or monster. Do not
  merge or treat one as reusable production content until its corresponding
  production contract is present in
  `tests/harness/core-regression-scenarios.json` and
  `scripts/verify-development.ps1` accepts every required stage.
- Player, weapon, and monster actions must invoke their assigned Rule Blocks. Direct
  action effects, Unity code, or Authority branches must not duplicate a
  Rule-Block-owned gameplay result. Removing the required Rule Block or
  semantic contract must remove the behavior without a name-based or visual
  fallback.
- Defeat, loot availability, collection, cleanup, and respawn are separate
  lifecycle behaviors. Each transition needs its own assigned Rule Block or an
  explicit post-Rule invocation with its own binding. A conditional post-Rule
  may be optional so a non-fatal attack still succeeds, but removing that
  block must remove its lifecycle result; an attack action must not silently
  own all later transitions.
- Unity combat presentation is selected from projected semantics such as
  `Damageable` and canonical presentation intents. A prefab, mesh, catalog
  category, or scene object name must never be the switch that adds targeting,
  health, hit, death, or loot behavior.
- Player jump is an Authority-approved ephemeral action, not a local input
  shortcut. The world avatar must author `jump_action`,
  `jump_takeoff_speed`, `gravity_acceleration`, and
  `ground_stick_velocity`, and must be assigned the invoked jump Rule Block.
  Grounded state is a transient Unity collision observation; it is never
  persisted as a per-frame world Fact.
- Normal player locomotion, gravity, jump, Authority reconciliation, and
  controller-mode impact must be combined into exactly one
  `CharacterController.Move` call per frame. A temporary Rigidbody reaction
  may own the transform only after the CharacterController owner is suspended,
  and ownership must return explicitly after the reaction settles. The two
  transform owners must never run concurrently.
- Once fixed-tick Authority movement is enabled, a client collision-resolved
  pose is an ephemeral ordered observation only. It must never overwrite the
  Authority motion state. Shared movement advances from canonical intent,
  evaluated action occurrences, authored movement tuning, Zone bounds, and
  authored collision proxies; removing any required semantic contract must
  fail closed without a Unity-coordinate fallback.

## 2. Authority and data rules / 권한·데이터 규칙

- Unity communicates with the World Authority API only; never connect Unity
  directly to PostgreSQL or Redis.
- Durable world edits must use revisioned, idempotent authority commands.
- Per-frame input, observations, movement samples, and live presence are
  ephemeral. They must not create durable database events every frame.
- Preserve the difference between authored/static facts, observed facts, and
  inferred facts.
- Canonical ontology IDs are English. Localized labels and aliases are never
  written into rules or saved facts.

## 3. Change discipline / 변경 작업 원칙

- Preserve unrelated user changes, scene hierarchy changes, UI layout work, and
  third-party assets. Never reset or broadly overwrite them.
- Prefer project-owned scenes, prefabs, ScriptableObjects, and data assets over
  changing third-party demo assets.
- For a behavior change, record both the enabled case and the disabled/removed
  case in the relevant test or change record.
- Every new or migrated gameplay vertical slice must include executable
  evidence for both the complete Triple -> Rule Block -> Physical Meaning path
  and the Rule-Block-removed path before it is used as a reusable production
  contract.
- Do not use a visual patch as evidence of a system fix. Verify the data,
  rule, adapter, and runtime result in that order.

## 4. Required verification / 필수 검증

1. Run `scripts/verify-development.ps1` for documentation, server-build, and
   harness-manifest checks.
2. When local Docker services are expected, add `-RequireServices`.
3. For Unity code/data behavior, run the relevant EditMode or PlayMode suite
   using `scripts/run-unity-harness-tests.ps1`.
4. After Unity script changes, confirm the Unity Console has no new errors.
5. Update the change record when a product/system decision changes.

## 5. Documentation and language / 문서·언어 규칙

- System policy or development-plan changes must update both
  `docs/PROJECT_CONTEXT.md` and `docs/PROJECT_CONTEXT.ko.md` in the same
  change.
- Create English and Korean versions together for new project-facing process,
  policy, or planning documents. Code/API identifiers remain canonical English.
- Start a decision/change record from `docs/changes/CHANGE_RECORD_TEMPLATE.ko.md`
  when a change crosses system boundaries or changes product policy.

## 6. Harness assets / 하네스 구성

- `tests/harness/core-regression-scenarios.json`: canonical core scenarios and
  their executable/manual evidence, including the mandatory
  `player-ontology-production`, `weapon-ontology-production`, and
  `monster-ontology-production` contracts.
- `scripts/verify-development.ps1`: fast development environment check.
- `scripts/run-unity-harness-tests.ps1`: Unity test runner wrapper.
- `scripts/verify-unity-mcp.ps1`: verifies the persistent local Streamable HTTP
  endpoint and connected Unity project. Keep local TOV Unity MCP on
  `http://127.0.0.1:8080/mcp`; do not revert it to per-task stdio.
- `docs/changes/`: concise decision/change evidence.

한국어 요약: 데이터의 주인을 먼저 정하고, 의미는 트리플/프로필/규칙으로
표현하며, Unity는 그 결과를 표현한다. 변경 뒤에는 켜진 경우와 제거된 경우를
모두 검증하고, 구조 결정은 한글·영문 컨텍스트 문서에 함께 기록한다.
