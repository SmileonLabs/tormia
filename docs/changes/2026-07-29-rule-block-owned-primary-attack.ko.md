# 룰블록이 소유하는 기본 공격

## 요약

- **날짜:** 2026-07-29
- **담당:** TOV 개발 하네스
- **상태:** 구현 및 검증 완료
- **관련 요청 또는 이슈:** 온톨로지 정책에 맞는 마우스 왼쪽 몬스터 공격

## 의도

첫 재사용 근접 공격을 완전한 생산 라인 계약으로 만듭니다. 무기 트리플이
능력과 수치를 선택하고, 배정된 룰블록이 결과를 소유하며, Authority가 이를
평가하고 Unity는 승인된 결과만 표현합니다.

## 데이터 분류

- [x] 영속 작성 월드 데이터
- [x] 런타임 관측
- [x] 추론 상태
- [x] Unity 표현
- [x] 전송 / 권한 / 인프라

## 결정과 경계

`MeleeAttackOnPrimaryIntent`가 체력·사망·보상 변경을 소유합니다. `attack`
액션은 명령 범위 Intent와 생명체 ID만 전달합니다. 행동 ID·피해량·거리·
쿨다운은 무기 Fact가 소유합니다. Unity 타게팅·애니메이션·VFX 코드는 피해를
만들거나 룰블록 부재를 우회할 수 없습니다.

## 온톨로지 표현

- 무기 Fact: `attack_action`, `attack_damage`, `attack_range`,
  `attack_cooldown`, `grants_capability -> MeleeAttack`
- 바인딩: `tool has_rule_block MeleeAttackOnPrimaryIntent`
- Intent: `actor primary_attack_intent target` (일시적)
- 장착 조건: `tool equipped_by actor`
- 대상 조건: `Damageable`, `Hostile`, `is_alive -> true`
- 결과: 룰블록 소유 체력 조정과 조건부 사망·보상 Fact
- 의미·표현: `HandheldWeapon`, `AttackLight`, 전투 카탈로그 VFX

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 활성화 / 추가 | 도구에 배정된 규칙이 피해를 평가하고 투영 입력 경로가 열린다 | `PrimaryAttackRuleBindsToToolAndOwnsDamage`, `AttackInputResolvesActionAndRuleFromAuthorityProjection` |
| 비활성화 / 제거 | 도구의 블록을 제거하면 평가와 입력 라우팅이 모두 사라진다 | `RemovingPrimaryAttackRuleBlockRemovesAttackBehavior`, `AttackInputResolvesActionAndRuleFromAuthorityProjection` |
| 회귀 / 예외 | 거리·쿨다운은 도구 Fact에서 오고 패키지·Manifest·카탈로그 버전이 일치한다 | `AttackRangeAndCooldownResolveFromToolFacts`, `AttackCooldownRuntimeRejectsImmediateDuplicate`, `PublishedDevelopmentActionsContainEquipAndGuardedDeath` |
| 기존 월드 마이그레이션 | 버전 4가 없는 공격 predicate만 한 번 추가하고 기존 값이나 이후 제거 상태를 보존한다 | `AttackFactIntroductionRunsOnceAndPreservesAuthoredValue` |
| 실제 Authority 경로 | 격리 월드에서 규칙을 발행·배정하고, 제거 시 공격을 거부하며, Fact 소유 쿨다운과 조건부 사망·보상을 검증한다 | `scripts/run-combat-authority-smoke.ps1` |

2026-07-29 검증 결과:

- World Authority 테스트: 37개 통과
- Unity EditMode 전투/애니메이션 테스트: 36개 통과
- Unity EditMode 의미/Authority 역할 테스트: 20개 통과
- 격리 Authority 전투 스모크: 통과
- 동기화 후 Unity Console 컴파일 오류: 0개

## 성능과 멀티플레이 영향

대상 위치와 쿨다운 상태는 일시적입니다. Redis는 키 기반 복제 서버 공용 임대를
사용하며 프레임별 DB 쓰기는 없습니다. 승인된 규칙 결과만 revisioned 월드
이벤트 스트림에 들어갑니다.

## 갱신한 문서

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] 테스트 시나리오 목록
