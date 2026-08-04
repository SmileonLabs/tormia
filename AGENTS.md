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
- Player melee damage must pass through an Authority-owned ephemeral attack
  occurrence. Input may request an occurrence and Unity may report a projected
  contact candidate during authored timing, but Authority must revalidate the
  current actor/tool/target contract and authored collision proxies, consume
  the occurrence exactly once, and only then invoke the immutable damage action
  and its assigned Rule Block. Direct generic action execution, renderer-bounds
  contact generation, and replayed contact must fail closed.
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
- `scripts/verify-unity-mcp.ps1`: verifies Unity's official Assistant MCP
  relay, the project-pinned Editor bridge/named pipe, and Codex config. Use
  `-LiveToolProbe` only for an approved, interactive client session; normal
  harness runs must not create a fresh approval request on every invocation.
  Use `%USERPROFILE%\.unity\relay\relay_win.exe --mcp --project-path
  <absolute-project-path>` as the primary TOV Unity MCP transport. The former
  `http://127.0.0.1:8080/mcp` server is a temporary migration fallback only and
  must not be restored as the primary transport after official relay validation.
- `docs/changes/`: concise decision/change evidence.

한국어 요약: 데이터의 주인을 먼저 정하고, 의미는 트리플/프로필/규칙으로
표현하며, Unity는 그 결과를 표현한다. 변경 뒤에는 켜진 경우와 제거된 경우를
모두 검증하고, 구조 결정은 한글·영문 컨텍스트 문서에 함께 기록한다.

## 7. Multi-agent operating policy / 서브 에이전트 운영 정책

앞으로 TOV 프로젝트 작업은 작업 규모와 위험도에 따라 서브 에이전트를
적극적으로 구성한다. 단, 작은 문구 수정이나 명확한 단일 파일 버그에는
불필요하게 서브 에이전트를 만들지 않는다. 이 절은 위의 제품·온톨로지·권한·
검증 규칙을 대체하지 않으며, 모든 에이전트는 그 규칙을 먼저 준수해야 한다.

최종 목표:

- 온톨로지 정책을 지키면서 기능을 확장한다.
- 임시 덧대기, 이름 기반 예외, 숨은 fallback을 만들지 않는다.
- Unity, Authority, 데이터, 하네스 경계를 명확하게 유지한다.
- 구현 전 원인을 확인하고 구현 후 활성·제거 경로를 모두 검증한다.
- 장기적으로 멀티플레이와 UGC 확장이 가능한 구조를 유지한다.

### 7.1 기본 역할 구성

**A. 메인 통합 책임자**

- 권장 모델: `gpt-5.6-sol`, 추론 강도 `high`.
- 사용자 요구를 작업 단위로 분해하고 파일 소유 범위를 지정한다.
- 조사 결과와 구현 결과를 교차 검토하며 공용 Core 계약과 최종 통합을
  담당한다.
- 중복 코드와 숨은 우회 경로를 제거하고 최종 빌드·테스트·Unity Console을
  검증한다.
- 시스템 전체 재설계, 반복되는 구조적 버그, 보안·Authority·데이터 무결성이
  함께 얽힌 경우에만 `xhigh` 또는 `ultra`를 제한적으로 사용한다.

**B. 온톨로지 아키텍트**

- 권장 모델: `gpt-5.6-sol`, 추론 강도 `high`.
- 다음 생산라인을 먼저 설계하고 검증한다:
  `trigger -> canonical intent/observation -> authored Triple -> assigned Rule
  Block -> rule evaluation -> origin-classified result -> Authority
  persistence/projection -> Meaning/Profile -> Unity Adapter -> player
  experience`.
- 계정 프로필, 영속 월드 데이터, 런타임 관측, 추론 결과, Unity 표현의
  소유권을 구분한다.
- Rule Block 제거 시 동작과 소유 결과가 사라지는지 확인한다.
- 캐시나 어댑터가 숨은 규칙 엔진이 되지 않도록 하고 이름·메시·프리팹 기반
  예외를 금지한다.
- 최초 설계 감사는 읽기 전용으로 수행한다.
- 증분 평가, 런타임 계약 캐시, Authority 전체 구조, 멀티플레이 이동·전투
  권한, 대규모 마이그레이션, 시스템 전체 성능 감사에만 `ultra`를 제한적으로
  사용한다.

**C. Authority·서버 엔지니어**

- 권장 모델: `gpt-5.6-sol`, 추론 강도 `high`.
- 기본 담당 범위: `server/`, `infrastructure/`, 서버 테스트.
- World Authority 판정, PostgreSQL·Redis 영속성, Revision, 멱등 명령,
  NPC·몬스터 Zone Tick을 담당한다.
- Unity가 최종 피해·권한·상태 전환을 결정하거나 매 프레임 영속 DB 쓰기를
  만들지 못하게 한다.
- 전체 Projection 재조회와 과도한 네트워크 전송을 점검한다.
- Rule Block 실행 결과와 직접 Action 효과가 중복되지 않게 한다.

**D. Unity 런타임 엔지니어**

- 권장 모델: `gpt-5.6-terra`, 추론 강도 `medium`; 복잡한 물리·성능 문제는
  `high`.
- 기본 담당 범위: `Assets/Scripts/Ontology/Unity/`,
  `Assets/Scripts/Ontology/UI/`, Unity 전용 테스트, 프로젝트 소유 Prefab과
  Scene.
- 입력은 canonical intent만 발생시키고 Unity는 Authority와 온톨로지 결과를
  표현한다.
- Unity Physics, CharacterController, Animator 등 안정적인 엔진 기능을
  사용하되 사용 권한은 Triple·Rule Block·Meaning이 결정한다.
- `Update`/`LateUpdate`의 전체 검색과 불필요한 할당을 피한다.
- 사용자 Scene·UI 하이어라키·Inspector 값을 보존하고 외부 에셋 원본보다
  프로젝트 소유 Prefab을 수정한다.

**E. 하네스·품질 검수자**

- 권장 모델: `gpt-5.6-sol`, 추론 강도 `high`.
- 구현 담당과 독립적으로 검토하며 최초 검수는 읽기 전용으로 수행한다.
- 이름·프리팹·메시 하드코딩, Rule Block 없는 fallback, 직접 피해·장착·
  죽음·리스폰, 중복 규칙·상태 소유자, 제거 경로 누락, 마이그레이션 누락,
  매 프레임 전체 평가, 반복 전체 검색, 과도한 DB·Redis·네트워크 호출,
  캐시 무효화, 멀티플레이 권한을 전수 검사한다.
- 활성 경로와 제거 경로 모두에 실행 가능한 증거를 요구한다.

모델 지정은 현재 Codex 런타임에서 해당 모델과 추론 강도를 지원할 때 적용한다.
서브 에이전트 모델을 명시적으로 다르게 지정할 때는 전체 대화 상속 대신 필요한
최근 맥락이나 독립 지시문만 전달하고, 프로젝트 문서와 현재 파일을 직접 읽게 한다.

### 7.2 작업 규모에 따른 운영

- 작은 작업: 문구 수정, 단일 UI 위치, 명확한 한 파일 버그는 메인이 직접
  처리한다.
- 중간 작업: Unity와 온톨로지처럼 두 영역이 연결되면 관련 에이전트 1~2개를
  사용한다. 조사와 구현 또는 검수를 분리한다.
- 큰 작업: Unity·Authority·온톨로지·데이터가 함께 변경되면 최초 읽기 전용
  병렬 감사 후 메인이 범위를 확정한다. 서버와 Unity는 파일 소유권을 나눠
  병렬 구현하고 마지막에 독립 하네스 검수를 수행한다.
- 동시 슬롯이 부족하면 온톨로지 아키텍트, Authority 엔지니어, Unity
  엔지니어 순으로 배치하고, 완료된 자리를 하네스 검수자로 교체한 뒤 메인이
  통합한다.

### 7.3 필수 작업 순서

1. 현재 코드·데이터·씬 상태를 읽는다.
2. 사용자 변경과 기존 미커밋 변경을 확인한다.
3. 재현 증거와 실제 호출 경로를 확인한다.
4. 추측과 확인된 원인을 구분한다.
5. 온톨로지 생산라인과 데이터 소유권을 설계한다.
6. 활성 경로와 제거 경로를 정의한다.
7. 파일 소유 범위를 나눈다.
8. 서버와 Unity를 충돌하지 않는 범위에서 구현한다.
9. 공용 계약과 데이터 자산을 메인이 통합한다.
10. 하네스 검수자가 독립 검토한다.
11. 발견된 우회·중복·하드코딩을 제거한다.
12. 한글·영문 컨텍스트와 변경 기록을 함께 갱신한다.
13. 개발 하네스, 서버 빌드, Unity 테스트를 실행한다.
14. Unity Console에서 새로운 오류가 없는지 확인한다.
15. 실제 게임에서 사용자가 확인할 테스트 순서를 제공한다.

### 7.4 파일 충돌 방지

- 여러 에이전트가 같은 파일을 동시에 수정하지 않는다.
- 공용 Core 파일은 기본적으로 메인이 소유한다.
- 서버 에이전트는 Unity Scene과 UI를 수정하지 않는다.
- Unity 에이전트는 서버 데이터 모델을 임의로 변경하지 않는다.
- 하네스 검수자는 검토가 끝나기 전 구현 파일을 수정하지 않는다.
- 다른 에이전트의 변경을 reset, checkout, overwrite하지 않는다.
- 사용자가 수정한 Scene·UI·Inspector 값을 보존한다.
- 충돌이 예상되면 병렬 작업을 중단하고 순차 작업으로 전환한다.

### 7.5 온톨로지 필수 정책

- 새로운 재사용 행동은 Rule Definition과 Rule Block을 먼저 만든다.
- 입력과 Unity 코드는 최종 게임 관계를 직접 만들지 않는다.
- `has_rule_block` 확인만 하고 같은 결과를 직접 적용하지 않는다.
- Rule Block 제거 시 해당 동작과 소유 결과가 제거되어야 한다.
- Physical Meaning은 필요한 물리 동작에만 사용한다.
- 애니메이션·UI·대화 전용 결과도 평가된 의미 결과가 필요하다.
- Prefab, Mesh, Animation, Input, MonoBehaviour만으로 완성된 콘텐츠로
  취급하지 않는다.
- 플레이어·무기·몬스터 생산 계약과 하네스 증거를 유지한다.
- 캐시는 평가 결과만 빠르게 제공하며 새 규칙을 추론하거나 누락된 권한을
  보완하지 않는다.
- 캐시 무효화는 Triple, Rule Block, Profile, package version과 연결한다.

### 7.6 성능 기본 정책

- 매 프레임 전체 트리플과 전체 규칙을 평가하지 않는다.
- 변경된 Triple과 관련 Rule Block만 증분 평가한다.
- 반복 평가 결과는 런타임 계약으로 컴파일하고 캐시하며 생성·적중·무효화
  지표를 남긴다.
- 위치, Grounded, 접촉, 애니메이션 프레임은 영속 Fact로 저장하지 않는다.
- Unity `Update`/`LateUpdate`에서 전체 오브젝트 검색을 반복하지 않는다.
- NPC별 개별 네트워크 호출보다 Zone 단위 일괄 시뮬레이션을 사용한다.
- 전체 Projection은 입장·복구에 사용하고 일반 변경은 Revision delta를
  우선한다.
- 물리·전투 쿼리는 풀링과 NonAlloc을 우선하되 고정 버퍼 포화로 결과를
  누락하지 않는다.
- 최적화 전에 측정하고 측정된 병목만 수정한다.

### 7.7 검증 완료 기준

작업은 다음 조건을 모두 만족해야 완료다.

- 요구 기능의 활성 경로가 동작한다.
- 필요한 Rule Block을 제거하면 기능이 사라진다.
- 이름·프리팹·메시 기반 예외가 없다.
- Unity가 서버 권한 결과를 우회하지 않는다.
- 데이터의 영속·런타임·표현 소유권이 명확하다.
- 관련 서버 테스트와 Unity EditMode/PlayMode 테스트가 통과한다.
- `scripts/verify-development.ps1`이 통과한다.
- Unity Console에 새로운 오류가 없다.
- 한글·영문 컨텍스트와 변경 기록이 일치한다.
- 사용자가 직접 확인할 게임 테스트 절차가 제공된다.

### 7.8 사용자 보고 방식

- 중간 진행 보고를 반복하지 않고 중요한 단계 완료나 실제 차단 사항만 짧게
  알린다.
- 사용자 선택 없이 안전하게 진행할 수 있으면 완료 단위까지 계속한다.
- 설계 충돌, 데이터 손실 위험, 외부 권한이 필요한 경우에만 질문한다.
- 최종 보고에는 변경 내용, 유지된 온톨로지 흐름, 통과한 테스트, 남은 위험·
  미검증 항목, 게임 확인 방법만 명확하게 정리한다.

### 7.9 TCP/UDP 하이브리드 멀티플레이 정책

- 전송 방식은 게임 권한이 아니다. HTTPS, SignalR, UDP는 canonical intent,
  observation, 평가된 result와 snapshot만 운반하며 규칙이나 상태 전환을
  만들거나 누락된 권한을 보완하지 않는다.
- 계정, 월드 편집, Rule/Triple, 장착, 전투 결과, 생명주기, 인벤토리와 저장은
  Authority 소유의 신뢰성 있는 TCP 명령 또는 이벤트로 유지한다.
- UDP는 대체 가능한 고빈도 이동 intent와 Authority motion snapshot에만 우선
  사용한다. 패킷 하나의 유실은 최신 패킷 또는 신뢰성 있는 복구 경로로
  회복할 수 있어야 한다.
- 모든 UDP 입력은 활성 `RuntimeSessionId`, world, Zone, actor ownership,
  transport generation, protocol version, 단조 증가 sequence와 정확히 평가된
  semantic contract에 종속된다. Bearer 토큰을 datagram에 넣지 않는다.
- 전송 ACK는 배달 확인일 뿐이다. 게임 처리 완료 경계는 Authority의
  `LastProcessedIntentSequence`와 서버 Tick이다.
- TCP/UDP 전환 중 입력 writer는 하나만 활성화한다. 이전 generation, 이전
  session, 중복·역순·재생 패킷은 공유 상태를 바꾸지 못한다.
- motion delta 누락은 존재 제거가 아니다. 인증된 complete recovery snapshot만
  presence를 조정할 수 있다.
- 로컬 예측은 영속·공유 결과를 만들지 않으며 ordered input acknowledgement로
  보정한다. 원격 플레이어는 Authority timeline을 지연 보간하고 무제한 외삽,
  Unity 임의 추격 속도와 stale-session rewind를 금지한다.
- 공격, 피해, 사망, 리스폰, 장착과 loot는 UDP만으로 확정하지 않는다. discrete
  presentation occurrence는 안정적인 occurrence ID와 Authority ordering을
  사용하되 게임 권한을 만들지 않는다.
- packet 인증, replay window, size/rate/numeric 검증은 ontology 평가와 공유 상태
  변경 전에 수행한다. UDP 장애는 Authority 우회가 아니라 신뢰성 있는 fallback,
  제한된 presentation 정지 또는 complete recovery로 처리한다.
- UDP 기능은 보안·손실·역순·재접속·Android/PC 외부망 하네스가 통과하기 전까지
  기본 비활성화한다. 각 단계는 한영 문서, 활성·제거 테스트, 개발 하네스와
  Unity Console 검증이 통과해야 다음 단계로 진행한다.
