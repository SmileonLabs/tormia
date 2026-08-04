# Authority 공격 발생 파이프라인

## 결정

자율 몬스터 공격은 더 이상 스케줄러 틱마다 피해를 즉시 주는 부수 효과가 아닙니다. World Authority가 몬스터에 배정된 Rule Block으로 불변 공격 액션을 미리 평가하고, 일시적인 `AttackOccurrence`를 만든 뒤 작성된 `attack_windup_seconds`, `attack_recovery_seconds` 트리플에 따라 `준비 -> 접촉 -> 회복` 단계를 진행합니다.

플레이어 근접 공격도 같은 경계를 사용합니다. 입력은 일시적인 공격 발생을 요청하고, Unity는 작성된 접촉 창 안에서 투영된 대상 후보만 보고합니다. Authority가 현재 의미 자격과 캡슐 접촉을 검증하고 발생을 한 번만 소비한 뒤 불변 피해 액션과 배정 Rule Block을 실행합니다. 발생이 필요한 액션을 일반 명령으로 직접 실행하면 실패합니다.

접촉 시점에는 같은 불변 액션과 배정 Rule Block을 다시 실행합니다. 이때 현재 대상 자격, 생존, 적대 관계, 실시간 위치, 거리, 재사용 대기시간을 다시 검증하고 피해를 정확히 한 번만 적용합니다. 영속 변경이 승인되면 revision 알림을 즉시 보내며, Unity는 투영된 애니메이션과 체력 결과만 표현하고 피해를 계산하지 않습니다.

## 제거 경로

자율 또는 플레이어 공격 Rule Block, 액션 연결, 접촉 수치, 대상 생명주기, 진영 관계 또는 충돌 계약을 제거하면 공격 결과도 사라집니다. 렌더러·프리팹·애니메이션 이름이나 Unity 피해 fallback은 없습니다.

## 증거

- `AutonomousActorOntologyContractTests`
- `PlayerAttackOccurrenceRuntimeTests`
- `OntologyAutonomousMonsterContractTests`
- `OntologyCombatVerticalSliceAssetTests`
- `monster-ontology-production`, `weapon-ontology-production` 하네스 계약
