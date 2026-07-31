# 룰블록으로 제한되는 무기 장착

- 날짜: 2026-07-28
- 상태: 구현 완료
- 범위: World Authority, Unity 온톨로지 데이터, 물리 표현, 하네스

## 결정

무기 장착은 튜브와 같은 온톨로지 생산 계약을 사용합니다. 무기는 정식
트리플, `AutoCarryNearbyCarryable` 룰블록, `RightHandCarry` 부착
프로필, `HandheldWeapon` 물리 프로필이 모두 있을 때만 장착할 수
있습니다.

불변 `equip_weapon` 행동은 활성 룰블록을 요구합니다. World Authority는
룰블록 바인딩을 행동 평가용 온톨로지 스냅샷에 투영하며, Unity
컨트롤러나 오브젝트 이름 예외에 의존하지 않습니다.

## 마이그레이션

- 개발 패키지 `1.7.0`에서 `AutoCarryNearbyCarryable`을 발행합니다.
- `equip_weapon` 정의 버전 `5`는
  `has_rule_block -> AutoCarryNearbyCarryable`을 요구합니다.
- 시작용 검 3종은 `physical_profile -> HandheldWeapon`을 작성하고
  기본 룰블록을 받습니다.
- 기존 편집 가능 월드는 일회성 의미 계약 표식을 기록하기 전에
  리비전 명령으로 누락된 카탈로그 트리플과 룰블록을 복구합니다.

## 검증 근거

- Unity EditMode:
  `WeaponCatalogUsesTripleRuleBlockAndPhysicalMeaningContract`
- 서버 단위 테스트:
  `EquipWeapon_ReplacesForwardRelationAndMaintainsInverseRelation`
- 서버 비활성 테스트:
  `EquipWeapon_WithoutRuleBlockIsRejected`
- 하네스:
  `weapon-ontology-vertical-slice`

## 후속 보완: 불변 룰블록 버전

현재 튜브·장착 룰블록의 조건이 작성되기 전에 로컬 카탈로그에 버전 1이
이미 게시되어 있었습니다. 따라서 현재 정의들은 버전 2를 사용하며,
개발 패키지 게시와 새 월드 바인딩이 동일한 카탈로그 버전을 참조합니다.
변경된 내용을 버전 1로 다시 게시하면 기존 월드 의미를 덮어쓰지 않고
거부합니다.
