# Authority 전투 데이터 소유권과 원자적 배치

## 요약

- **날짜:** 2026-07-28
- **담당:** TOV 개발팀
- **상태:** 검증
- **관련 요청:** 플레이어와 몬스터 전투를 온톨로지 정책에 맞게 개선

## 의도

첫 전투 루프를 이후 무기·몬스터·NPC에도 재사용할 수 있게 만듭니다.
콘텐츠 작성자는 메시·프리팹·오브젝트·몬스터 전용 코드 대신 템플릿
트리플과 발행된 행동 데이터를 바꿉니다.

## 데이터 분류

- [x] 영속 작성 월드 데이터
- [x] Unity 표현
- [x] 전송 / 권한 / 인프라

## 결정과 경계

World Authority가 엔티티 존재, 타입이 보존된 초기 Fact, 행동 검증, 피해,
사망, 드롭 가능 상태를 소유합니다. 무기 템플릿이 피해 수치를 소유하고
몬스터 템플릿이 생명력·적대성·진영·드롭 정체성을 소유합니다. Unity는
대상 선택 보조와 승인 결과 표현만 소유합니다.

Unity 전투 카탈로그가 최대 체력이나 드롭 ID를 중복 소유하면 안 됩니다.
공격 행동에 고정 피해량이나 몬스터 드롭 ID를 넣지 않습니다. Authority가
설정된 씬은 인증이 끝나기 전 로컬 스냅샷을 임시 원본으로 복원하지 않습니다.

## 온톨로지 표현

- 무기: `has_concept Sword`, `grants_capability MeleeAttack`,
  `damage_profile BasicSwordDamage`, `attack_damage 10`
- 몬스터: `has_concept Damageable`,
  `vitality_profile BeholderBasicVitality`,
  `combat_disposition Hostile`, `belongs_to_faction WildMonster`,
  `loot_table BeholderBasicLoot`,
  `loot_item OntologyDataFragment`
- 공격 효과: 무기의 `attack_damage * -1`로 대상 `current_health` 조절,
  최솟값 `0`
- 사망 전이: `is_alive=False`, `loot_status=Available`
- 표현: 승인된 `AttackLight` 뒤 최신 투영으로 피격·사망·VFX 표시

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 활성화 / 추가 | 타입이 보존된 엔티티 Fact가 원자적으로 배치되고 무기 피해가 대상 체력을 줄인다 | `run-isolated-combat-authority-smoke.ps1` |
| 비활성화 / 제거 | `attack_damage`, 적대성, 생존 상태 또는 패키지가 없으면 피해가 발생하지 않는다 | `AuthoritativeActionEvaluatorTests`와 격리 스모크 |
| 회귀 / 예외 | 재전송은 피해를 중복 적용하지 않고 체력은 0에서 멈추며 대상 소유 드롭은 유지된다 | 격리 스모크 |
| 투영 준비 | 최근 장착 입력과 승인된 애니메이션 의도는 설정된 짧은 준비 시간 동안만 유지된다 | `OntologyCombatVerticalSliceAssetTests` |
| 만료 / 재전송 | 만료된 장착 입력과 멱등 행동 재전송은 지연 장착이나 애니메이션을 발생시키지 않는다 | `OntologyCombatVerticalSliceAssetTests` |

## 성능과 멀티플레이 영향

초기 개념과 Fact가 배치 명령의 같은 트랜잭션·리비전을 사용하므로 투영
경합과 명령 왕복 수가 줄어듭니다. 프레임별 이동과 대상 선택은 계속
ephemeral이며 프레임별 DB 쓰기나 새 polling은 추가하지 않았습니다.
Unity는 설정된 준비 시간 안에서만 장착 후보를 로컬에서 재확인하며,
canonical 무기 대상이 생기기 전에는 영속 명령을 만들지 않습니다.

## 갱신한 문서

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] 언어팩 CSV
- [x] 테스트 시나리오 목록
