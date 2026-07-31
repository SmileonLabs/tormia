# Authority가 승인하는 빈 공간 휘두르기

## 요약

- **날짜:** 2026-07-29
- **담당:** TOV 개발 하네스
- **상태:** 플레이어 입력 경로는 대체됨, 런타임 계약은 유지
- **관련 요청 또는 이슈:** 대상이 없어도 일반적인 왼쪽 클릭 근접 공격
  애니메이션은 재생하되 대상 피해는 온톨로지가 계속 소유하도록 개선

## 의도

> 2026-07-29에 입력 경로가 대체되었습니다. 리비전 없는 런타임 휘두르기
> 계약은 재사용할 수 있도록 유지하지만, 공유 왼쪽 클릭은 이제 작성된 공격
> 사거리 안의 살아 있는 적대 대상이 없으면 이를 요청하지 않습니다.
> `2026-07-29-target-gated-primary-melee-click.ko.md`를 참고합니다.

게임플레이 권한을 Unity로 옮기지 않고 일반 액션 RPG와 같은 기본 공격 감각을
제공합니다. 빈 공간 왼쪽 클릭은 휘두르기 표현을 만들 수 있지만, 체력·사망·
보상은 별도의 대상 행동이 승인된 경우에만 변경할 수 있습니다.

## 데이터 분류

- [x] 영속 작성 월드 데이터
- [x] 런타임 관측
- [x] 추론 상태
- [x] Unity 표현
- [x] 전송 / 권한 / 인프라

## 결정과 경계

무기는 `swing_action -> swing_weapon`을 작성하고 의미 계약 버전 5를 통해
재사용 `SwingWeaponOnPrimaryIntent/?tool` 룰블록을 받습니다. 이 블록은
`runtimePresentation.actorAnimationIntent -> AttackLight`를 소유하며 의도적으로
영속 효과를 갖지 않습니다. World Authority는
`POST /v1/worlds/{worldId}/runtime/actions`에서 이를 평가하고, 변경 효과가
포함된 런타임 규칙은 거부하며 월드 리비전을 증가시키지 않습니다.

`MeleeAttackOnPrimaryIntent/?tool`은 계속 독립적인 피해 소유자입니다. Unity는
Authority 투영에서 유효한 적대 대상을 찾았을 때만 해당 행동을 요청합니다.
Unity는 휘두르기 표현을 피해로 바꾸지 않으며 피해 전송 정의도 애니메이션
선택을 중복하지 않습니다.

## 온톨로지 표현

- 트리거: `Player/Attack`의 마우스 왼쪽
- 무기 트리플: `tool swing_action swing_weapon`
- 휘두르기 바인딩: `tool has_rule_block SwingWeaponOnPrimaryIntent`
- 휘두르기 의도: `actor primary_swing_intent actor` (일시적)
- 휘두르기 결과: `AttackLight` 런타임 표현, 영속 변경 없음
- 피해 바인딩: `tool has_rule_block MeleeAttackOnPrimaryIntent`
- 피해 의도: `actor primary_attack_intent target` (명령 범위)
- 영속 결과: 룰블록 소유 체력·조건부 사망·보상 변경

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 활성화 / 빈 공간 | Authority가 영속 변경·리비전 증가 없이 `AttackLight`를 승인한다 | `PresentationOnlySwingRuleAcceptsWithoutDurableMutation`, `scripts/run-combat-authority-smoke.ps1` |
| 활성화 / 유효 대상 | 휘두르기 승인 뒤 별도 피해 룰블록만 대상을 변경할 수 있다 | `AttackInputResolvesActionAndRuleFromAuthorityProjection`, `scripts/run-combat-authority-smoke.ps1` |
| 비활성화 / 제거 | 휘두르기 블록을 제거하면 빈 공간 표현만 사라지고 피해 블록은 독립적으로 남는다 | `AttackInputResolvesActionAndRuleFromAuthorityProjection` |
| 잘못된 런타임 규칙 | 데이터 변경을 가진 런타임 행동은 커밋 전에 거부한다 | `WorldRepository.EvaluateRuntimeAction`의 런타임 변경 차단 |
| 애니메이션 생산 라인 | 룰블록 소유 `AttackLight`가 검증된 Manifest·Database·ActorProfile 투영에서 해석된다 | `PublishedDevelopmentActionsContainEquipAndGuardedDeath`, `CurrentAnimationAssetsMatchManifestProjection` |

2026-07-29 검증 결과:

- World Authority 테스트: 38개 통과
- Unity EditMode 전투/애니메이션 테스트: 36개 통과
- 격리 Authority 전투 스모크: 통과
- 동기화 후 Unity Console 컴파일 오류: 0개

## 성능과 멀티플레이 영향

빈 공간 휘두르기는 일시적인 Authority 평가와 Redis 쿨다운 임대만 만듭니다.
월드 이벤트·Fact·리비전을 만들지 않습니다. 대상 피해는 기존 리비전·멱등
명령 경로를 유지합니다.

## 갱신한 문서

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] 테스트 시나리오 목록
