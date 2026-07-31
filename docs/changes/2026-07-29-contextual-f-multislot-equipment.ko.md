# 장착 입력과 다중 슬롯 소유권

날짜: 2026-07-29

## 결정

TOV는 검증된 착용형 근접 흐름을 유지하고 `F`는 운반형 무기에만
사용합니다. 착용형은 선택·접근, 일시적 `interaction_intent`,
`AutoEquipNearbyWearable`을 거쳐 Actor가 대상에 도착하면 장착합니다.
무기는 `equip_weapon` World Authority 행동을 사용합니다.

정식 영속 소유 관계는 다음과 같습니다.

```text
item --equipped_by--> actor
item --has_slot-----> canonical slot
```

착용 추론은 착용 트리플과 `AutoEquipNearbyWearable`을 요구하고,
`equip_weapon`은 무기·운반 트리플과 `AutoCarryNearbyCarryable`을
요구합니다. 같은 작성 슬롯의 두 번째 아이템은 거부하지만 Waist와 RightHand는
함께 사용할 수 있습니다. 불변 `equip_wearable` 버전 1 정의는 패키지
`1.8.0` 이력에 남지만 일반 Unity 입력은 더 이상 `F`를 이 행동으로
연결하지 않습니다.

`unequip_equipment`은 선택한 아이템의 `equipped_by` 관계만 제거하며, 해당
대상에 구형 배우 소유 `equipped_item` 관계가 남아 있으면 함께 철회합니다.

## 표현 경계

Unity는 `SelectThenEquip`을 로컬 온톨로지 관찰·룰 엔진에 연결하고,
`SelectThenCarry`를 Authority 무기 행동에 연결할 뿐 장착 권한을 판단하지
않습니다. 부착은 `equipped_by`를 따릅니다. 무기 애니메이션은 장착 엔티티
중 데이터 소유 전투 표현 카탈로그에 등록된 대상만 선택하므로 비무기 장비가
무장 이동 자세를 바꾸지 않습니다.

왼쪽 클릭은 착용형을 선택·접근하고 일시적 `interaction_intent`를
발행합니다. 장착된 착용형을 클릭하면 기존 온톨로지 해제 행동을 실행합니다.
`F`는 튜브를 선택·장착·해제하지 않습니다.

## 실행 증거

- 서버: `EquipWeapon_CoexistsWithEquipmentInAnotherSlot`,
  `EquipWeapon_WhenSameSlotIsOccupiedIsRejected`,
  `EquipWearable_WithRuleBlockCreatesItemOwnedRelation`,
  `EquipWearable_WithoutRuleBlockIsRejected`
- Unity EditMode: `OntologyEquipmentSlotConditionTests`,
  `CombatFDoesNotRouteWearablesAwayFromProximityRule`,
  `PublishedDevelopmentActionsContainEquipAndGuardedDeath`,
  `RightHandWeaponUsesGenericDataDefinedAttachmentContract`,
  `WeaponCatalogUsesTripleRuleBlockAndPhysicalMeaningContract`
- Unity PlayMode: `OntologyAttachmentAdapterTests`와 씬 구성 스모크

대응 룰블록을 제거하면 해당 행동이 사라지며 Unity에는 이름 기반 허용
fallback이 없습니다.
