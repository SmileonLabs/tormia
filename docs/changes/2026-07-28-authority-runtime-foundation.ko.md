# Authority 런타임 기반 준비

## 요약

- **날짜:** 2026-07-28
- **상태:** 구현 및 통합 검증
- **범위:** 월드 생성, 아바타 입장, 일시적 모션, 장착 행동, 구형 관계 마이그레이션

## 의도

월드는 플레이 가능한 것처럼 보이지만 거리 제한 Authority 행동이 플레이어
서버 위치를 찾지 못하는 상태를 방지합니다.

## 데이터 소유권

- Zone 정의, 아바타 체크포인트 Zone 연결, `movement_speed`, 장착 관계는
  World Authority의 영속 데이터입니다.
- 플레이어 모션은 Redis의 만료되는 런타임 상태입니다.
- Unity는 명령을 보내고 결과를 표현하지만 최종 거리 판정을 소유하지 않습니다.

## 결정과 경계

편집 가능한 Zone 없는 월드는 `define_zone`으로 프로젝트 설정의 시작 Zone을
준비합니다. 등록 아바타에는 `movement_speed`가 없을 때만 추가하고,
`save_avatar_checkpoint`로 연결합니다. 입장은 일시적 모션 상태가 조회될
때까지 기다립니다.

기존 체크포인트 Transform은 유지합니다. 저장된 Zone이 유효하면 우선
사용하고, Zone이 없거나 휴면이면 저장 위치로 대체 Zone을 결정해 Zone 연결만
보정합니다.

Unity `OntologyObject` 표현 갱신이 끝나지 않은 순간에는 현재 Authority
투영의 canonical `has_concept -> Weapon` 행으로 장착 후보를 찾습니다.
프리팹명이나 템플릿명으로 무기 의미를 추측하지 않습니다.

전투 버튼은 활성 상태에서 직렬화된 Input System 액션의 `performed` 이벤트를
구독합니다. 장착과 공격은 컨트롤러 `Update`의 한 프레임
`WasPressedThisFrame` 폴링이 우연히 맞는 것에 더 이상 의존하지 않습니다.

`migrate_legacy_equipment_relations`는 활성 상태인 행동 생성 `equips` Fact만
철회합니다. 과거 기록만으로 현재 장착을 증명할 수 없으므로 새 관계를 추측해
만들지 않습니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 활성 | Zone, 아바타 모션, 정식 장착, 공격, 애니메이션 의도가 순서대로 완료됨 | `scripts/run-combat-authority-smoke.ps1` |
| 제거 | 런타임 위치가 없으면 거리 제한 장착을 전송하지 않음 | `EquipCommandRequiresAuthorityRuntimePosition` |
| 구형 | 마이그레이션 후 행동 생성 `equips`가 남지 않음 | 전투 Authority 스모크 |
| 이어하기 | 런타임 보정이 체크포인트 Transform과 유효 Zone을 유지함 | `RuntimeZoneResolutionPreservesCheckpointZoneAndPosition` |
| 표현 갱신 | Unity 의미 컴포넌트 갱신 중에도 투영된 canonical Weapon을 선택할 수 있음 | `EquipCandidateUsesCanonicalAuthorityConceptDuringPresentationRefresh` |

## 갱신 문서

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
