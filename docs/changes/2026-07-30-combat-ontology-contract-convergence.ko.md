# 전투 온톨로지 계약 통합

## 요약

- **날짜:** 2026-07-30
- **담당:** TOV 개발
- **상태:** 구현 및 검증 완료
- **범위:** 플레이어, 무기, 자율 몬스터, 사냥, 표현, 전리품

## 의도

사냥 루프를 사용자가 작성하거나 생성한 임의의 엔티티에도 재사용할 수 있게
합니다. Unity 컴포넌트, 프리팹명, 입력 바인딩, Authority 엔드포인트가 작성
데이터와 배정 Rule Block이 소유할 행동을 몰래 대신하지 못하게 합니다.

## 데이터 분류

- 영속 작성 월드 데이터: 능력, 액션 ID, 수치, 프로필, Rule Block 배정,
  생명주기 상태
- 런타임 관측: 입력 시작점, 위치, 포인터 후보, 무기 접촉
- 추론·표현 상태: 애니메이션, VFX, 대상·처치 표현
- Authority 전송: 공유·영속 결과는 revision 멱등 처리, 이동 의도는 휘발 처리

## 결정과 경계

표준 계약은 다음과 같습니다.
`Triple -> Rule Block -> 불변 액션 -> World Authority -> 결과 ->
Meaning/Profile -> Unity Adapter`.

장착·해제, 이동·점프, 타깃·추적·공격, 처치·전리품, 수집은 서로 분리된
재사용 계약입니다. 공격은 명시적인 후속 Rule 호출로만 전리품 규칙을
호출할 수 있습니다. 이 호출은 비치명 공격을 허용하도록 조건부이지만,
블록을 제거하면 전리품 활성화도 제거됩니다. Unity는 Projection 의미를
해석하고 결과만 표현하며,
대상 자격이나 영속 상태를 만들지 않습니다.

## 온톨로지 표현

- 무기: `equip_action`, `unequip_action`, `interaction_range`, 전투 수치,
  부착·물리 프로필
- 플레이어: `locomotion_action`, `jump_action`, 이동·달리기 속도,
  `jump_height`, `gravity_acceleration`, `AuthorityKinematic`
- 몬스터: `target_concept`, `targeting_profile`, `chase_profile`,
  `target_action`, `chase_action`, 공격·생명주기 액션 ID,
  타깃·추적·공격·전리품 Rule Block. 타깃과 추적은 불변 평가 전용
  액션입니다. 스케줄러는 휘발성 후보와 위치만 제공하고, 배정 Rule이
  자격과 이동 허용을 결정합니다. Rule 바인딩은 스케줄러에 고정된 Rule ID
  목록이 아니라 각 불변 액션 정의에서 해석합니다.
- 전리품: `loot_status`, `loot_collected_by`는 처치 대상 소유 영속 출처
- 표현: `Damageable`과 canonical 애니메이션·VFX 의도가 Unity Adapter를 선택

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 활성화 | 호환 플레이어·무기·몬스터가 공통 Authority 파이프라인으로 동작 | Unity EditMode·PlayMode와 서버 단위 테스트 |
| 제거 | 액션 Triple, Rule Block, 프로필, 의미를 제거하면 해당 행동만 제거 | 하네스 Manifest의 제거 경로 테스트 |
| 마이그레이션 | 기존 배정은 불변 버전으로 이동하되 사용자 제거 블록은 복원하지 않음 | 에셋 마이그레이션 단언과 동기화 데이터 |
| 자율 제어 | 타깃·추적 액션 미리보기는 각 배정 Rule이 있을 때만 통과하고 영속 변경을 만들지 않음 | `TargetAndChaseExecuteTheirAssignedRuleDefinitions` |

## 성능과 멀티플레이 영향

프레임 단위 이동·타기팅 관측·표현은 휘발로 유지합니다. 체력·장착·생명·
전리품 출처는 revision 멱등 결과로 유지하며 자율 이동은 World Authority의
Zone 스케줄링을 계속 사용합니다.

## 최종 검증

- World Authority 테스트: 52/52 통과
- Unity EditMode: 242/242 통과
- Unity PlayMode: 54/54 통과
- 빈 격리 데이터베이스 전투 Authority 스모크: 통과
- Docker 서비스와 지속형 Unity MCP를 포함한 개발 하네스: 통과
- 런타임 소스에서 무기·몬스터·프리팹 식별자 분기와 고정 자율 Rule ID
  목록이 없음을 확인

## 갱신한 문서

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] `tests/harness/core-regression-scenarios.json`
- [x] `AGENTS.md`
