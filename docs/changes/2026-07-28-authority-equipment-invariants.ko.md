# Authority 장착 불변 조건

## 요약

- **날짜:** 2026-07-28
- **담당:** Codex, Smileon Labs
- **상태:** 구현 및 검증 완료
- **관련 요청:** 검 장착 하드코딩과 온톨로지 정책 위반 제거

## 의도

무기 장착과 해제를 완전한 Authority 소유 상태 전이로 만듭니다.
거리·독점 점유·역방향 관계·해제가 신뢰된 Unity 클라이언트나 무기 프리팹
이름에 의존하지 않게 합니다.

## 데이터 분류

- [x] 영속 작성 월드 데이터
- [x] 런타임 관측
- [ ] 추론 상태
- [x] Unity 표현
- [x] 전송 / 권한 / 인프라

## 결정과 경계

발행된 액션 정의가 장착 의미를 소유합니다. Authority는 이동 레지스트리의
ephemeral 아바타 위치와 월드 엔티티의 영속 대상 Transform을 읽고 범용
런타임 제약을 평가한 뒤, 하나의 리비전 명령에서 양쪽 장착 관계를 적용합니다.
Unity는 입력 대상 선택과 부착 표현만 소유합니다.

정지 상태의 이동 정보는 월드 리비전이나 영속 Fact를 만들지 않고 Redis TTL만
갱신합니다. 무기 카탈로그 제작은 편집 가능한 콘텐츠 매니페스트를 읽으며,
외부 프리팹 이름에서 의미를 추론하지 않습니다.

## 온톨로지 표현

```text
avatar equipped_item weapon
weapon equipped_by avatar
weapon can_equip True
weapon has_concept Weapon
```

`equip_weapon`은 데이터에 선언된 역방향 predicate와 함께 정방향 관계를
설정합니다. `unequip_weapon`은 두 관계를 제거합니다.
`maxActorTargetDistance`는 월드 Fact가 아닌 범용 비영속 런타임 제약입니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 활성화 | 가까운 미점유 무기에 두 관계가 생성된다 | `EquipWeapon_ReplacesForwardRelationAndMaintainsInverseRelation` |
| 비활성화 | 점유됐거나 먼 무기는 변경 없이 거부된다 | `EquipWeapon_OccupiedByAnotherActorIsRejected`, `RuntimeDistanceConstraint_RequiresAvailableNearbyPositions` |
| 제거 | 해제 시 두 관계가 제거되고 월드 표현으로 돌아간다 | `UnequipWeapon_RemovesForwardAndInverseRelations`, Unity 장착 테스트 |
| 제작 | 무기 정의는 매니페스트를 사용하고 GripPoint가 필수다 | `WeaponCatalogIsBackedByEditableContentManifest`, `WeaponProfileRequiresAuthoredGripPoint` |

최종 검증에서 Authority 서버 테스트 11개, Unity EditMode 테스트 22개,
Unity PlayMode 테스트 12개, Unity Console 오류 없음,
`scripts/verify-development.ps1 -RequireServices`가 모두 통과했습니다.

## 성능과 멀티플레이 영향

장착 시 직렬화된 월드 명령 안에서 ephemeral 이동 상태 한 번과 대상 Transform
한 번을 읽습니다. 정지 이동 상태는 초당 한 번 Redis TTL만 갱신하고 리비전을
방송하지 않습니다. 월드 잠금으로 두 Actor가 같은 무기를 동시에 점유하지
못하게 합니다.

## 갱신한 문서

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [ ] 언어팩 / migration
- [x] 회귀 시나리오 목록
