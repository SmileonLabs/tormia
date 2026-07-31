# 플레이어 온톨로지 생산 계약

## 요약

- 날짜: 2026-07-29
- 영역: 플레이어 월드 의미, 일시적 이동, Authority 평가, Unity 표현,
  개발 하네스
- 상태: 구현 및 검증

## 의도

실제 사냥 전체 루프를 승인하기 전에 플레이어도 무기·몬스터와 같은 재사용
가능한 온톨로지 생산 원칙을 따르게 합니다. Unity 컨트롤러나 Authority
엔드포인트가 아바타를 움직일 수 있다는 이유만으로 플레이어 이동이
존재해서는 안 됩니다.

## 데이터 분류

- 계정 프로필: 외형, 템플릿, 계정 설정만 소유
- 영속 월드 데이터: 플레이어 역할, 능력, 생명, 진영, 액션, 이동 수치,
  물리 의미, 애니메이션 의도, Rule Block 배정
- 런타임 관측: 입력 샘플, 접지, 위치, 이동 상태
- Unity 표현: 로컬 충돌, 승인된 예측 이동, 보정, 애니메이션, 피드백

## 결정과 경계

플레이어 의미 계약 버전 2는 불변 `move_avatar` 액션과 배정된
`MovePlayerFromIntent` Rule Block을 도입합니다. 모든 런타임 이동 입력은
해당 액션을 지정합니다. World Authority는 일시적 샘플을 받기 전에 액션과
배정 규칙을 평가하고, 이동 스케줄러도 같은 의미 계약을 독립적으로
요구합니다.

Unity는 Authority 승인 뒤 로컬 이동을 표현할 수 있지만 이동 허가를
소유하지 않습니다. 계정 입장은 새로 도입된 계약 단계만 마이그레이션하며,
런타임 기반 준비 과정은 더 이상 모든 기본 플레이어 Fact를 복원하지
않습니다. 따라서 버전이 기록된 뒤 사용자가 제거한 의미는 제거 상태로
남습니다.

## 온톨로지 표현

- 트리플: `has_concept -> Actor`, `has_concept -> PlayerControlled`,
  `grants_capability -> Locomotion`, `locomotion_action -> move_avatar`,
  숫자형 `movement_speed`, `is_alive -> true`,
  `physical_profile -> AuthorityKinematic`, 대기·이동 애니메이션 의도
- Rule Block: `MovePlayerFromIntent/?actor`
- 액션: `move_avatar@1`
- 패키지: `social_village@3.3.0`
- 물리 의미: `AuthorityKinematic`

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 완전한 계약 | 이동 액션과 규칙이 영속 변경 없이 일시적 이동 요청을 승인 | `PlayerLocomotionRuleAcceptsCompleteEphemeralContract`; `AvatarTriplesDeclareCompleteGroundLocomotionMeaning` |
| 규칙 또는 액션 제거 | Authority 평가와 Unity 예측 이동 경로가 함께 사라짐 | `RemovingPlayerLocomotionRuleBlockRemovesMovementBehavior`; `RemovingLocomotionActionFactRemovesUnityIntentRoute` |
| 제거 후 재입장 | 버전 관리된 런타임 입장이 제거된 의미를 다시 작성하지 않음 | `RuntimeFoundationDoesNotReauthorRemovedPlayerSemantics` |

## 성능과 멀티플레이 영향

이동 샘플은 계속 일시적이며 월드 리비전을 올리지 않습니다. 현재 각 승인
샘플은 표준 부작용 없는 액션 사전 평가 경로를 사용합니다. 스케줄러는
1초마다 자격 있는 아바타를 갱신하고 모호하거나 불완전한 계약을 거부합니다.
향후 불변 평가 입력을 캐시해 최적화하더라도 이 의미 경계는 바꾸지 않습니다.

## 갱신한 문서

- `AGENTS.md`
- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
- `scripts/verify-development.ps1`
