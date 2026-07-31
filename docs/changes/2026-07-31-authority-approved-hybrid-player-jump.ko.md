# Authority 승인형 하이브리드 플레이어 점프

## 요약

- **날짜:** 2026-07-31
- **담당:** TOV 게임플레이/온톨로지
- **상태:** 구현 및 검증
- **관련 요청:** 덧대기 방식으로 돌아가지 않고 CharacterController 이동과 분리된 Rigidbody 반응을 사용해 플레이어 점프를 복구합니다.

## 의도

작성 데이터와 배정된 Rule Block이 권한을 부여하고, World Authority가
일시적인 접지 요청을 승인하며, Unity는 하나의 충돌 소유자로 승인된
포물선 이동을 표현하는 재사용 가능한 플레이어 점프 생산 계약을
제공합니다.

## 데이터 분류

- 영속 월드 데이터: 점프 액션 ID, 이륙/중력/접지 튜닝, 충격 응답 의미,
  배정된 Rule Block
- 런타임 관측: 접지 상태, 수직 속도, 임펄스 속도
- Unity 표현: CharacterController 충돌, 애니메이션, 임시 Rigidbody 반응
- Authority 전송: 평가 전용 액션 요청과 결과

## 결정과 경계

`JumpPlayerFromIntent`가 점프 권한을 소유합니다. Unity는 Space 입력만으로
점프하지 않습니다. 일반 이동은 프레임당 정확히 한 번의
CharacterController Move를 사용합니다. 임시 Rigidbody 반응은
CharacterController가 중단된 동안에만 Transform을 소유할 수 있습니다.
접지와 속도 값은 일시적이며 영속 이벤트를 만들지 않습니다.
새 접지 제약은 `false`일 때 canonicalization에서 제거하므로 기존 불변 액션
버전의 checksum을 유지하고, `true`는 점프 액션의 불변 내용으로 남습니다.
이전 점프 Rule과 액션 버전 1은 불변 이력으로 유지합니다. 하이브리드
계약은 패키지 3.8.1의 버전 2로 발행하고, 플레이어 의미 계약 버전 6이
이력을 덮어쓰지 않고 활성 바인딩을 이관합니다.

## 온톨로지 표현

- 관계: `jump_action`, `jump_takeoff_speed`,
  `gravity_acceleration`, `ground_stick_velocity`,
  `impact_response_profile`
- 능력: `Jump`
- 액션: `jump_avatar`
- Rule Block: `JumpPlayerFromIntent`
- 물리 의미: `AuthorityKinematic`과 `ControllerImpulse` 또는
  `TemporaryRigidbodyReaction`

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 활성화 | 전체 작성 계약과 참인 접지 관측이 Authority 승인을 받고 작성된 포물선 이동을 시작합니다. | Unity `OntologyPlayerProductionContractTests`, 서버 `GroundedRuntimeConstraintRejectsMissingOrNegativeObservation` |
| 비활성화 | `jump_action`이나 Rule Block을 제거하거나 접지 관측을 보내지 않으면 로컬 fallback 없이 점프가 제거/거절됩니다. | Unity 제거 경로 테스트와 서버 런타임 제약 테스트 |
| 회귀 | 중력·착지·컨트롤러 임펄스가 하나의 Move를 공유하고 임시 Rigidbody 소유권은 배타적이며 새 기본 필드가 기존 액션 충돌을 만들지 않습니다. | CollisionFlags·임펄스 감쇠 EditMode, 하이브리드 PlayMode, 액션 canonicalization 서버 테스트와 Unity 컴파일/콘솔 검증 |
| 기존 월드 | 점프 Rule 버전 1에 연결된 아바타는 이전 바인딩을 철회하고 버전 2를 배정받은 뒤 월드에 입장합니다. | 실제 Authority 발행 HTTP 200, DB 활성 바인딩 버전 2·버전 1 철회, 런타임 입장 `IsWorldRuntimeReady` 확인 |

## 성능과 멀티플레이 영향

프레임별 월드 Fact나 DB 이벤트를 추가하지 않습니다. 점프 요청은 평가
전용입니다. Authority가 지형 충돌 표현을 가지기 전까지 수직 충돌
포물선은 로컬 표현이며, 이 변경은 서버가 원격 수직 이동을 시뮬레이션한다고
주장하지 않습니다.

## 갱신한 문서

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `AGENTS.md`
- `tests/harness/core-regression-scenarios.json`
