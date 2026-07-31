# 대상 제한 기본 근접 공격 클릭

## 요약

- **날짜:** 2026-07-29
- **담당:** TOV 개발 하네스
- **상태:** 검증 완료
- **관련 요청 또는 이슈:** 왼쪽 클릭 이동을 유지하고 클릭한 몬스터가 무기
  사거리 안에 있을 때만 공격

## 의도

무기를 장착했다는 이유만으로 일반 이동 클릭을 전투가 가져가지 않게 합니다.
포인터가 투영된 살아 있는 전투 대상을 가리킬 때만 왼쪽 클릭을 전투 경로로
사용합니다. 이후 추가된 대상 고정 접근 계약이 사거리 밖 전투 대상을 지형
이동으로 새지 않게 접근시키는 방법을 정의합니다.

## 데이터 분류

- [ ] 계정 프로필
- [x] 영속 작성 월드 데이터
- [x] 런타임 관측
- [ ] 추론 상태
- [x] Unity 표현
- [x] 전송 / 권한 / 인프라

## 결정과 경계

무기의 `attack_range` 트리플, 배정된 공격·휘두르기 룰블록, 대상의 Authority
소유 적대·생존 Fact가 공유 클릭을 전투로 보낼 수 있는지 결정합니다. Unity는
투영된 포인터 후보와 장착 도구에 작성된 행동 ID만 전달합니다. World
Authority는 실행과 같은 준비 경로를 사용하는 부작용 없는 사전 평가로 두
행동을 검사합니다. 사전 평가는 변경 적용, 쿨다운 획득, 리비전·이벤트·Fact
기록을 하지 않습니다. 승인된 실행은 표현이나 영속 피해 전에 같은 계약을
다시 평가합니다.

빈 바닥, 우호 또는 죽은 대상, 누락된 룰블록, 없거나 모호한 사거리 데이터는
클릭을 이동 입력으로 남깁니다. 사거리 밖의 투영된 살아 있는 전투 대상은
대상 고정 접근 계약이 보관하고 작성된 사거리로 접근합니다.
메시·프리팹·템플릿·오브젝트 이름은 사용하지 않습니다.

## 온톨로지 표현

- 트리거: 공유 마우스 왼쪽 클릭
- 도구 트리플: `tool attack_range positive-number`
- 도구 행동: `tool attack_action attack`,
  `tool swing_action swing_weapon`
- 도구 바인딩: `MeleeAttackOnPrimaryIntent/?tool`,
  `SwingWeaponOnPrimaryIntent/?tool`
- 대상 Fact: `target has_concept Damageable`,
  `target combat_disposition Hostile`,
  `target is_alive True`
- 런타임 관측: 포인터 대상, Authority 런타임 Actor·대상 위치
- Authority 경로: 배정된 두 룰블록의 부작용 없는 사전 평가
- Authority 결과: 승인된 표현 뒤 리비전이 있는 룰블록 소유 피해

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 활성화 / 추가 | 작성된 사거리 안의 살아 있는 적대 대상을 Authority 사전 평가가 승인하고 공격 경로를 따른다 | `AttackInputPublishesAuthoredActionsWithoutLocalRuleEvaluation`, `PresentationOnlySwingRuleAcceptsWithoutDurableMutation` |
| 비활성화 / 제거 | 필요한 룰블록 또는 적대 트리플을 제거하면 Authority 사전 평가가 거절하고 fallback 없이 클릭을 반환한다 | `RemovingPrimaryAttackRuleBlockRemovesAttackBehavior`, `PrimaryAttackRejectsFriendlyOrDefeatedTarget` |
| 회귀 / 예외 | 빈 바닥은 사전 평가를 만들지 않고 사거리 밖의 살아 있는 전투 대상은 대상 고정 접근 경로가 보관한다 | `AttackInputPublishesAuthoredActionsWithoutLocalRuleEvaluation`, `OntologyPlayerInputPriorityTests`, `AttackRangeAndCooldownResolveFromToolFacts`, `CombatRejection_OutOfRangeApproachesButCooldownDoesNotMove` |

2026-07-29 검증 결과:

- TOV 개발 하네스, Docker Authority 빌드, 서비스 상태·헬스: 통과
- 현재 Unity 런타임 어셈블리와 Unity EditMode 테스트 어셈블리 Roslyn
  컴파일: 통과
- Unity EditMode 전투·입력 우선순위 테스트: 50개 통과, 0개 실패
- World Authority 단위 테스트: 40개 통과, 0개 실패
- 리비전·체력을 바꾸지 않는 공격 사전 평가와 룰블록 제거·재적용을 포함한
  격리 전투 Authority 스모크: 통과
- 스크립트 새로고침 뒤 Unity Console 오류: 0개

## 성능과 멀티플레이 영향

입력 어댑터는 클릭할 때만 기존 포인터 레이캐스트 한 번을 수행합니다. 투영
후보가 있으면 공격 사전 평가 한 번을 요청하고, 이것이 승인된 경우에만
휘두르기 사전 평가를 한 번 요청합니다. 빈 바닥은 요청을 만들지 않습니다.
대상 고정 접근은 도착하거나 제한된 처리 중·쿨다운 간격이 지난 뒤에만
재평가할 수 있습니다. 프레임별 DB 이벤트, Fact, 쿨다운 임대, 리비전을
만들지 않으며 멀티플레이 평가자와 실행 검증자는 계속 World Authority입니다.

## 갱신한 문서

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [ ] 필요 시 언어팩 CSV / migration
- [x] 테스트 시나리오 목록
