# 계약 범위 기반 공격 후 이동

## 요약

- **날짜:** 2026-07-29
- **담당:** Authority 플레이어 이동과 Unity 애니메이션 표현
- **상태:** 구현
- **관찰 문제:** 승인된 공격 직후 이동하면 정지와 재개가 반복되고,
  캐릭터가 공격 자세를 유지한 채 미끄러지듯 이동했습니다.

## 의도

Unity가 이동이나 공격 권한을 만들지 않으면서 전투 후 이동을
부드럽게 유지합니다.

## 데이터 분류

- 몬스터 체력과 생명 변경은 영속 월드 데이터입니다.
- 플레이어 입력과 이동 승인은 비영속 런타임 상태입니다.
- 애니메이션 중단 여부는 검증된 Manifest에 작성하는 Unity 표현
  메타데이터입니다.

## 결정과 경계

Projection 리비전 자체를 이동 계약 변경으로 간주하지 않습니다.
Unity는 선택한 월드/Zone, 아바타 자신의 Fact, 배정된 Rule Block,
유일한 불변 이동 액션만 지문화합니다. 몬스터 피해처럼 관계없는
변경은 기존 이동 승인을 유지하고, 플레이어 계약이 변경·중복·누락·
제거된 경우에는 승인을 폐기합니다.

`Anim_Sword_LightAttack`은 원본 Manifest와 생성된 런타임 Database에서
중단 가능으로 명시했습니다. 따라서 승인된 이동은 공격 자세를 남긴
채 몸만 이동시키지 않고 카탈로그 소유 `WeaponWalk` 표현을 선택합니다.
이 메타데이터는 Authority 액션 평가나 피해 결과를 바꾸지 않습니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 몬스터 피해 리비전 | 기존 이동 승인 유지 | `UnrelatedMonsterRevisionPreservesApprovedLocomotionContract` |
| 플레이어 계약 변경 또는 제거 | Authority 재승인 전까지 예측 이동 중단 | `ChangedOrRemovedPlayerLocomotionContractRequiresReapproval` |
| 공격 후 이동 | Manifest에 작성된 공격 표현이 이동 표현에 양보 | `SwordLightAttackYieldsPresentationToApprovedLocomotion` |
| 중단 불가 표현 | 작성된 표현 잠금 유지 | `EquipmentTransitionMetadataAllowsImmediateLocomotion` |

## 성능과 멀티플레이 영향

계약 지문은 매 프레임이 아니라 영속 Projection을 받을 때만 계산합니다.
다른 엔티티의 Fact는 제외하고 네트워크 요청을 추가하지 않으며,
공유 멀티플레이 게임 결과의 소유자는 계속 Authority입니다.

## 갱신 문서

- `PROJECT_CONTEXT.md`
- `PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
