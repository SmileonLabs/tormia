# 튜브 온톨로지 계약

## 요약

- **날짜:** 2026-07-28
- **담당:** Codex, Smileon Labs
- **상태:** 구현 완료, Unity 런타임 검증 대기
- **관련 요청:** 튜브 흐름을 먼저 복구하고 이를 무기 이관에 사용할
  트리플 -> 룰블록 -> 물리 의미 기준으로 확립

## 의도

Unity 또는 오브젝트명 fallback 없이 튜브 장착과 임시 수영 기술을
복구합니다. 룰블록이 있을 때의 행동과 제거했을 때 행동도 함께 사라지는 것을
실행 가능한 테스트로 증명하는 첫 기준 계약으로 만듭니다.

## 데이터 분류

- [ ] 계정 프로필
- [x] 영속 작성 월드 데이터
- [x] 런타임 관측
- [x] 추론 상태
- [x] Unity 표현
- [x] 전송 / 권한 / 인프라

## 결정과 경계

등록된 월드 아바타가 작성된 `Actor` 월드 역할을 소유합니다. 튜브는 의미·물리
프로필 트리플과 버전이 있는 룰블록 바인딩을 소유합니다. 근접과 상호작용 의도는
ephemeral 관측입니다. 규칙 추론이 장비 슬롯·장착·임시 기술 결과를 소유하고,
Unity 어댑터는 근접 측정과 평가된 장착/물리 표현만 담당합니다.

개발 Rule 정의를 오브젝트 바인딩보다 먼저 발행합니다. 룰블록 바인딩 ID는
결정적입니다. Rule 발행 전에 만들어진 엔티티는 범용 카탈로그 이관으로 한 번만
복구하고 모든 바인딩이 성공한 뒤 `semantic_contract_version`을 기록합니다.
따라서 이후 사용자가 의도적으로 룰블록을 제거해도 다시 생기지 않습니다.

무기 Authority 행동은 이 완료 슬라이스의 범위 밖입니다. 다음 단계에서 검증된
룰블록 게이트형 계약으로 이관해야 합니다.

## 온톨로지 표현

```text
avatar has_concept Actor
tube has_concept Wearable
tube physical_profile LightBuoyant
tube attachment_profile WaistInflatableRing
tube has_slot Waist
tube pickup_behavior SelectThenEquip
tube grants_skill Swimming
tube has_rule_block AutoEquipNearbyWearable
tube has_rule_block EquippedItemGrantsTemporarySkill
```

런타임 추론은 아래 관계를 생성하고 조건이 사라지면 철회합니다.

```text
EquipmentSlot_{actor}_{Waist} equipped_item tube
tube equipped_by avatar
avatar has_temporary_skill Swimming
```

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 활성화 | Actor 의도·근접·완전한 튜브 데이터가 장착과 임시 Swimming을 만든다 | `InflatableRingVerticalSliceRequiresTripleRuleBlockAndPhysicalMeaning` |
| 비활성화 | `AutoEquipNearbyWearable` 제거 시 슬롯·장착·임시 기술이 철회된다 | 같은 PlayMode 테스트 |
| Authority 아바타 | 신규 배치와 복구가 canonical Actor를 유지한다 | `NewAvatarPlacementAuthorsCanonicalActorTriple`, `RuntimeFoundationDetectionUsesCanonicalFactsOnly` |
| 여러 Actor | NPC Actor가 로컬 플레이어 근접 해석을 중단시키지 않는다 | `LocalPlayerRoleWinsWhenSeveralActorsExist` |
| 재전송 | 엔티티·Rule·변수 조합이 안정된 바인딩 ID를 만든다 | `RuleBindingIdentityIsDeterministicAndDataScoped` |

`scripts/verify-development.ps1 -SkipServerBuild`는 통과했습니다. Unity MCP
인스턴스가 다시 연결된 뒤 전체 Unity 테스트와 Console 검증을 완료해야 합니다.

## 성능과 멀티플레이 영향

복구는 카탈로그 기본값이 있고 계약 마커가 없는 편집 가능 레거시 엔티티에만
한 번 실행됩니다. 정상 입장은 복구 DB 쓰기를 만들지 않습니다. 근접은 계속
주기가 제한된 ephemeral Unity 관측이며 프레임별 영속 이벤트를 만들지 않습니다.

## 갱신한 문서

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [ ] 언어팩 / migration
- [x] 회귀 시나리오 목록
