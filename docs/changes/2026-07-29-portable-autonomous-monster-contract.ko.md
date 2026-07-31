# 재사용 가능한 자율 몬스터 계약

## 요약

- 날짜: 2026-07-29
- 범위: 영속 월드 온톨로지, World Authority 런타임, Unity 표현
- 상태: 구현 완료, 서버·개발 하네스·실시간 Unity EditMode 검증 완료

## 의도

몬스터 행동을 Beholder 프리팹에 묶지 않고 어떤 배치 엔티티에도 적용할 수
있는 재사용 온톨로지 계약으로 만듭니다.

## 소유권 결정

작성된 컨셉과 Fact가 자율 전투 능력과 수치를 선언하고, 배정된 Rule Block이
공격 행동을 켭니다. `AuthorityKinematic` 물리 의미가 서버 소유 이동
어댑터를 선택합니다. World Authority가 목표, 이동, 쿨다운, 피해, 사망을
평가하며 Unity는 승인된 이동과 애니메이션 의도만 표현합니다.

## 완전한 경로

`autonomous_melee_monster` 프리셋이 컨셉, 진영·적대·생명·수치 Fact,
`attack_action`, canonical 애니메이션 의도, Rule Block 배정, 물리 의미를
작성합니다. 선택된 액션 정의가 호출하는 Rule Block은 액터에 배정된
Rule Block과 일치해야 합니다. Authority는 액터를 스케줄하고 승인된 피해를
리비전·멱등 월드 명령으로 적용합니다. Unity는 일시적인 액터 위치를 보간하고
Manifest/Database/Profile 생산 라인으로 애니메이션을 해석합니다.

## 제거 경로

Rule Block, 필수 작성 의미, 액션-규칙 연결, 물리 의미 중 하나를 제거하면
엔티티가 자율 시뮬레이션에서 제외됩니다. 프리팹, 메시, 표시 이름, Unity
어댑터 fallback은 행동을 복구하지 않습니다.

## 기존 배치 인스턴스

Beholder 기본 콘텐츠의 의미 계약 버전을 3으로 올립니다. 계약 마커가 없는
기존 인스턴스에는 현재 카탈로그의 완전한 계약을 한 번 적용하고, 버전이
있는 인스턴스에는 전환 구간에 선언된 Fact와 Rule Block의 도입·폐기만
적용합니다. 버전 3은 같은 predicate에 다른 활성 값이 있을 때만 구형 작성
기본값 `current_health=30`, `is_alive=true`를 폐기합니다. 따라서 충돌하는
구형 상태를 정규화하면서 죽은 몬스터를 되살리거나 유일한 정상 상태를
지우지 않습니다. 버전이 올라간 뒤 사용자가 제거한 항목은 누락된
기본값으로 간주해 복구하지 않습니다.

계약 도입은 canonical 관계 카디널리티를 따릅니다. `has_concept`는 집합값
관계로 선언되어 어떤 컨셉이 하나 존재하는지만 보지 않고 정확한 값의
중복을 검사합니다. 단일값 또는 미지정 predicate는 기존 작성값을
계속 보존합니다. 계약 마커가 없는 구형 인스턴스 복구도 같은
카디널리티를 사용하므로 기존 단일값 상태 옆에 카탈로그 기본값을 추가하지
않습니다.

## 검증

- 서버 계약 테스트: 승인 피해, Rule Block 제거, 대상 진영 자격, 제한된 이동.
- Unity EditMode 테스트 추가: 재사용 프리셋, Beholder 데이터 투영, 개발 패키지,
  Manifest/Profile 애니메이션 레퍼토리, 표현 어댑터의 몬스터 이름 분기 부재,
  일반 카탈로그 오브젝트에 몬스터 의미가 실수로 붙지 않는지 검증.
- Unity Core, Runtime, Editor, EditMode 테스트 어셈블리는 Unity가 생성한
  Roslyn 응답 파일로 오류 없이 컴파일했습니다.
- 실시간 Editor MCP에서 계약 마이그레이션 및 관련 전투·Authority 테스트
  53/53이 통과했습니다. 실제 Authority 투영의
  `current_health={0,30}`, `is_alive={false,true}`, 계약 버전 2 상태가
  `current_health=0`, `is_alive=false`, 계약 버전 3으로 정규화됐습니다.
  정상 몬스터는 계약 버전 3으로 올라가면서도 `current_health=30`,
  `is_alive=true`를 유지했습니다. 이후 전체 EditMode 회귀 테스트도
  202/202 통과했습니다.
