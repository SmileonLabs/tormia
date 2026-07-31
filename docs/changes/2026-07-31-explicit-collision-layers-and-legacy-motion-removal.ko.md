# 명시적 충돌 레이어와 레거시 이동 제거

## 요약

- **날짜:** 2026-07-31
- **담당:** TOV 개발 하네스
- **상태:** 구현, Unity Editor 실행 검증 대기
- **관련 요청:** 완전한 서버 권한 이동으로 확장하기 전에 로컬 충돌 기반 안정화

## 의도

로컬 캐릭터 충돌 소유권을 명시적이고 재사용 가능한 구조로 만듭니다. 사용하지
않는 레거시 이동 컴포넌트를 제거하고 의미 충돌 역할을 프로젝트 소유 Unity
Physics Layer에 매핑하며 지지면이 자신의 의미를 명시하도록 합니다.

## 데이터 분류

- [ ] 계정 프로필
- [x] 영속 작성 월드 데이터
- [x] 런타임 관측
- [ ] 추론 상태
- [x] Unity 표현
- [x] 전송 / 권한 / 인프라

## 결정과 경계

Physical Meaning의 `OntologyCollisionRole`이 의미 원본입니다. Unity Physics
Layer는 작성된 표현 매핑이며 온톨로지 ID나 게임 동작 권한이 될 수 없습니다.
`OntologyCharacterMotionCoordinator`만 일반 로컬 CharacterController 이동을
소유합니다. 이번 단계는 충돌 시뮬레이션을 World Authority로 옮기지 않습니다.

## 온톨로지 표현

기존 `physical_profile` 관계가 `motionDriver`, `collisionRole`, 이동 튜닝을
가진 프로필을 선택하고 Unity Adapter가 여기서 파생됩니다. Physical Meaning을
제거하면 원래 Unity Layer를 복원하고 이동 lease를 제거합니다. Collider가
정적이라는 사실만으로 `WalkableSupport` 의미를 얻지 않습니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 활성화 / 추가 | 모든 충돌 역할이 하나의 실제 프로젝트 레이어에 매핑되고 로컬 캐릭터 의미가 ActorBody를 적용 | `OntologyRuntimeDataAssetTests.StoneCatalogUsesNonBuoyantDynamicPhysicalProfile`; `OntologySemanticAdapterSynchronizerTests.LocalCharacterPhysicalMeaningOwnsAndRemovesMotionLease` |
| 비활성화 / 제거 | 프로필 제거 시 원래 레이어를 복원하고 로컬 이동 lease 제거 | `OntologySemanticAdapterSynchronizerTests.LocalCharacterPhysicalMeaningOwnsAndRemovesMotionLease` |
| 회귀 / 예외 | 작성되지 않은 정적 Collider를 지지면으로 취급하지 않음 | `OntologyHybridCharacterMotionTests.UnauthoredStaticColliderIsNotImplicitWalkableSupport` |

## 성능과 멀티플레이 영향

충돌 매트릭스는 프로필 데이터베이스에서 한 번 적용합니다. Layer 배정은 의미
동기화 때만 일어나며 프레임 단위 할당·DB 쓰기·revision을 만들지 않습니다.
추후 서버 고정 Tick 충돌 Solver가 구현될 때까지 이동은 클라이언트 충돌 표현과
순서·Zone 경계 검증 Authority 중계 구조를 유지합니다.

## 갱신한 문서

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [ ] 필요 시 언어팩 CSV / migration
- [x] 필요 시 테스트 시나리오 목록
