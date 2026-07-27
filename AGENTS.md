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
  their executable/manual evidence.
- `scripts/verify-development.ps1`: fast development environment check.
- `scripts/run-unity-harness-tests.ps1`: Unity test runner wrapper.
- `docs/changes/`: concise decision/change evidence.

한국어 요약: 데이터의 주인을 먼저 정하고, 의미는 트리플/프로필/규칙으로
표현하며, Unity는 그 결과를 표현한다. 변경 뒤에는 켜진 경우와 제거된 경우를
모두 검증하고, 구조 결정은 한글·영문 컨텍스트 문서에 함께 기록한다.
