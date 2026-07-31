# 전투 대상 고정 접근

## 요약

- **날짜:** 2026-07-29
- **담당:** TOV 개발 하네스
- **상태:** 검증
- **관련 요청 또는 이슈:** 몬스터를 반복 클릭할 때 플레이어가 공격과 이동
  사이에서 덜덜거리는 현상 제거

## 의도

몬스터 클릭 하나를 전투 경로에 유지합니다. 사거리 밖이면 작성된 사거리를
사용해 접근하고, 처리 중·쿨다운 응답에서는 같은 클릭을 지형 이동으로
바꾸지 않습니다.

## 데이터 분류

- [ ] 계정 프로필
- [ ] 영속 작성 월드 데이터
- [x] 런타임 관측
- [ ] 추론 상태
- [x] Unity 표현
- [x] 전송 / 권한

## 결정과 경계

Unity는 관찰한 대상을 비영속 입력·이동 의도로 잠시 보관할 수 있습니다.
액션 사용 가능 여부, Rule Block 평가, 사거리, 적대성, 생존, 쿨다운, 피해,
애니메이션 의도는 계속 World Authority가 소유합니다. 이동 정지 반경은
장착 도구의 canonical `attack_range`를 읽으며, 작은 안쪽 여유값은
충돌·이동 허용 오차일 뿐 공격 권한을 만들지 않습니다.

`action_target_out_of_range`일 때만 접근합니다. 처리 중·쿨다운은 움직이지
않고 기다립니다. 규칙, 액션, 의미 또는 올바른 사거리가 없으면 비전투 클릭
경로로 돌아가므로 Rule Block을 제거하면 전투 행동도 함께 제거됩니다.

## 온톨로지 표현

- 트리플: `tool attack_range <양수 숫자값>`
- 트리플: `tool attack_action <canonical action id>`
- 트리플: `tool swing_action <canonical action id>`
- Rule Block: `MeleeAttackOnPrimaryIntent`,
  `SwingWeaponOnPrimaryIntent`
- Physical Meaning: 기존 `HandheldWeapon`
- Unity 어댑터는 영속 Fact를 만들거나 규칙을 평가하지 않습니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 활성화 / 추가 | 사거리 밖 전투 클릭은 작성된 사거리까지 접근하고 한 번 재평가 | `CombatApproach_UsesAuthoredRangeWithNavigationInset` |
| 비활성화 / 제거 | Rule Block이 없으면 접근이나 숨은 공격 행동이 활성화되지 않음 | `CombatRejection_RemovedRuleDoesNotFallBackToApproach` |
| 회귀 / 예외 | 처리 중·쿨다운은 이동으로 바뀌지 않고 직접 이동은 접근을 취소 | `CombatRejection_OutOfRangeApproachesButCooldownDoesNotMove`, `DirectMovement_CancelsPendingCombatApproach` |

## 성능과 멀티플레이 영향

접근 상태는 로컬 비영속 상태입니다. Authority 사전 평가는 도착하거나 제한된
처리 중·쿨다운 재시도 간격이 지난 뒤에만 다시 요청합니다. 프레임마다 Fact,
이벤트, 리비전 또는 DB 쓰기를 추가하지 않습니다.

## 갱신한 문서

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] 핵심 회귀 시나리오
- [ ] 언어팩 또는 migration 불필요
