# 런타임 생명주기와 월드 입장 수렴

## 요약

- **날짜:** 2026-07-30
- **담당:** TOV 개발 하네스
- **상태:** 구조 구현 및 Unity 실제 검증 완료
- **관련 요청 또는 이슈:** Rule Block 제거 잔여 데이터, 입장 시 공중 부양,
  죽은 자율 액터 추적, 전투 타깃, 이동 미끄러짐의 구조적 회귀 복구

## 의도

생명주기, 실시간 위치, 입장 지면 안착, 이동 애니메이션을 캐시된 Unity
상태나 플레이어의 첫 입력이 아니라 각각 선언된 소유자로 수렴시킵니다.

## 데이터 분류

- [ ] 계정 프로필
- [x] 영속 작성 월드 데이터
- [x] 런타임 관측
- [x] 추론 상태
- [x] Unity 표현
- [x] 전송 / 권한 / 인프라

## 결정과 경계

World Authority가 활성 생명주기 자격과 자율 스케줄링을 소유합니다. 런타임
레지스트리는 휘발성 실시간 위치를 소유합니다. 영속 Transform은 체크포인트
또는 정적 배치 데이터이며 누락된 실시간 이동을 대신하지 않습니다. Unity는
지면 충돌과 애니메이션 표현을 소유합니다. 입장 중에는 아바타 루트를 활성
상태로 유지하되 렌더러와 입력을 차단합니다. 영속 체크포인트 복원, 충돌
접지, 이전 입력·애니메이션 초기화, 체크포인트 확인을 순서가 고정된 하나의
트랜잭션으로 실행하고, 모두 성공한 뒤에만 런타임 게이트를 엽니다.

일반 런타임 게이트는 로컬 아바타 표현 경계 안의 GameObject를 소유할 수
없습니다. 아바타에 붙은 토스트 표현은 아바타 루트를 끄지 않고 Behaviour로
게이트합니다. 또한 입장 흐름은 입력과 렌더러를 열기 전에 배정된 이동 액션과
Rule Block에 대해 0 이동량의 휘발성 Authority 승인을 완료합니다.

명령 기반 애니메이션 해석에는 `execute_action` 결과만 참여하며, 다른
Authority 승인 명령은 이 대기열을 거치지 않습니다.

의미 패키지 제거는 소유권 범위로 유지합니다. 패키지 기여분은 원자적으로
철회하지만 무관한 작성·생명주기·튜닝·다른 소유자의 Fact는 보존합니다.

## 온톨로지 표현

- 자율 액터와 대상은 `is_alive=true`가 필수입니다.
- 타깃·추적·공격에는 계속 배정된 불변 액션, Rule Block, 의미 프로필,
  `AuthorityKinematic` 의미가 필요합니다.
- 몬스터·프리팹·메시·인스턴스 이름은 조회하지 않습니다.
- idle과 locomotion 애니메이션 의도는 휘발성 표현 결과입니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 활성화 / 추가 | 살아 있고 계약이 완성된 액터가 최신 런타임 대상 위치를 사용해 스케줄링됩니다. | `ArbitraryEntityWithCompleteContractCanAttackPlayerFaction`, `LiveRuntimeOwnerDoesNotFallBackToDurableSpawn` |
| 비활성화 / 제거 | 사망·계약 제거 시 자율 이동이 제거되고, 패키지 Rule Block 제거 시 소유 트리플·의미도 철회됩니다. | `ActorLosingOntologyEligibilityEvictsItsRuntimeMotion`, `run-meaning-package-authority-smoke.ps1` |
| 회귀 / 예외 | 영속 데이터를 먼저 복원하고, 아바타 루트를 활성 상태로 유지하며, 숨김 상태에서 충돌 준비·이전 입력·애니메이션 초기화·Authority 이동 승인을 마친 뒤 `InWorld` 이후에만 입력을 엽니다. 비액션 명령은 애니메이션 작업을 예약하지 않습니다. | `RuntimeGateNeverDisablesLocalAvatarPresentationBoundary`, `WorldEntryPresentationPreparesBeforeSessionActivation`, `PendingGroundingRetriesWhenControllerBecomesReady`, `SwordLightAttackYieldsPresentationToApprovedLocomotion`, `AcceptedAnimationIntentWaitsForPresentationReadinessButReplayDoesNot` |

## 성능과 멀티플레이 영향

기존 설정 캐시 갱신에서 집합 차이를 한 번 계산하고 오래된 휘발성 액터
레코드만 제거합니다. 프레임별 영속 쓰기는 추가하지 않았습니다. 입장 준비는
로컬 표현 상태일 뿐입니다. 복원 위치가 실제로 바뀐 경우에만 revision 기반
체크포인트 명령을 한 번 보냅니다.

## 실제 검증

연결된 Unity 6000.4.7f1 Editor에서 확인했습니다.

- Unity 재컴파일 뒤 Console 오류 0건
- 런타임 게이트 EditMode 테스트 2개와 입장·씬 구성 PlayMode 테스트 4개 통과
- 저장 계정과 선택 월드의 실제 입장 흐름 완료: 세션 `InWorld`, 입장 단계
  `Active`, 입력·애니메이션 활성, Authority 이동 승인 `true`,
  `CharacterController.isGrounded=true`, 수직 지지 바이어스 `-1`,
  애니메이션 의도 `Idle`
- 입장 뒤 Unity Console 오류·경고 0건

## 갱신한 문서

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] 핵심 회귀 시나리오
