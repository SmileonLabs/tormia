# 규칙 블록 우선 게임 로직 정책

## 요약

- **날짜:** 2026-07-29
- **담당:** TOV 개발 하네스
- **상태:** 구현 완료
- **관련 요청:** 새로운 게임 로직이 재사용 규칙 블록을 우회하지 못하게 방지

## 의도

새로운 재사용 게임 행동은 사용자가 다른 호환 오브젝트에도 조립할 수 있는 생산라인
부품이어야 합니다. 필요한 트리플과 같은 규칙 블록을 연결하면 오브젝트 전용 코드를
추가하지 않고 동일한 행동을 사용할 수 있어야 합니다.

## 데이터 분류

- [x] 영속 저작 월드 데이터
- [x] 런타임 관측
- [x] 추론 상태
- [x] Unity 표현
- [x] 전송 / Authority / 인프라

## 결정과 경계

재사용 게임 로직의 주인은 규칙 정의와 연결된 규칙 블록입니다. 입력과 전송은
트리거 및 canonical Intent/관측만 소유합니다. Authority 액션이
`has_rule_block`을 확인한 뒤 규칙 블록의 최종 결과를 직접 작성하는 방식은 숨겨진
중복 규칙이므로 허용하지 않습니다.

## 온톨로지 표현

- 트리거: canonical 입력 Intent 또는 런타임 관측
- 연결: `entity has_rule_block RuleBlockId`
- 결과: 연결된 규칙 정의가 생성
- 표현/물리: 평가된 결과를 소비

첫 실패 검사는 무기 장착을 대상으로 합니다. `equip_weapon`은 `equipped_by`를
직접 작성하면 안 됩니다. F 경로를 생산 기준으로 인정하기 전에 재사용 가능한
`EquipItemOnInteractionIntent` 규칙 블록이 장착 결과를 소유해야 합니다.

현재 실제 마이그레이션도 이 경계를 구현합니다.

- `equip_weapon`은 임시 `interaction_intent`만 운반합니다.
- World Authority가 대상에 정확히 연결된 바인딩과 불변 게시 규칙 정의를 평가합니다.
- 영속 변경에는 `source_rule_binding_id`를 기록하며, 바인딩이나 패키지를 제거하면
  그 바인딩이 소유한 결과도 회수합니다.
- 기존 카탈로그 무기는 의미 계약 버전 2와 데이터로 작성한 마이그레이션을 통해
  `AutoCarryNearbyCarryable/?object`에서
  `EquipItemOnInteractionIntent/?target`으로 전환됩니다.

모든 게임플레이 생산라인 하네스 시나리오는 트리거, Intent/관측, 트리플,
규칙 블록, 평가, 결과, 조건부 Authority 저장·투영, Meaning/Profile,
어댑터, 플레이어 경험의 소유자를 기록합니다. Authority 또는 물리 분기가 필요
없는 경우에도 조용히 생략하지 않고 해당 없음의 이유를 기록해야 합니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 활성화 / 추가 | Intent와 연결된 규칙 블록이 모든 호환 오브젝트의 결과를 생성 | `ReusableEquipmentLogicMustBeOwnedByRuleBlock`, `EquipWeapon_CoexistsWithEquipmentInAnotherSlot` |
| 비활성화 / 제거 | 규칙 블록이 없거나 제거되면 결과가 생성되지 않고 바인딩 출처의 영속 결과도 회수 가능 | `EquipWeapon_WithoutRuleBlockIsRejected`, `DerivedCleanupPreservesMatchingAuthorityDurableResult`, 하네스 시나리오 `rule-block-first-reusable-gameplay` |
| 회귀 / 예외 | 전송은 결과를 만들지 않고 직접 효과와 혼합할 수 없으며 생산라인 단계도 생략되지 않음 | `EquipWeaponTransportCarriesIntentWithoutCreatingResult`, `RuleInvocationCannotHideADirectEquipmentEffect`, `CanonicalGameplayPipelineStagesHaveExplicitOwners` |

## 성능과 멀티플레이 영향

이 정책은 polling이나 프레임별 DB 쓰기를 추가하지 않습니다. 적절한 입력 Intent는
임시 전송으로 유지하고, 영속 결과는 Authority 경계에서 리비전·멱등 방식으로
처리해야 합니다.

## 갱신한 문서

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] 테스트 시나리오 목록
- [x] `AGENTS.md`
