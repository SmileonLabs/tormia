# 데이터 기반 장착과 Transform 소유권

## 요약

- **날짜:** 2026-07-28
- **담당:** TOV 개발
- **상태:** 구현, Unity 런타임 검증 대기
- **관련 요청:** 전투 장착 하드코딩을 제거하고 장착한 아이템이 저장된 바닥
  Transform으로 끌려가는 문제를 구조적으로 제거

## 의도

플레이어와 NPC 장비가 하나의 재사용 가능한 온톨로지 장착 파이프라인을
사용하게 합니다. 전투 코드가 프리팹을 알아봐서 무기를 붙이는 것이 아니라,
Authority 관계가 선택된 장착 프로필의 계약과 일치할 때 장착합니다.

## 데이터 분류

- [ ] 계정 프로필
- [x] 영속 작성 월드 데이터
- [ ] 런타임 관측
- [x] 추론/액션 상태
- [x] Unity 표현
- [x] 전송 / 권한 / 인프라

## 결정과 경계

World Authority가 영속 장착 관계를 소유합니다. `OntologyAttachmentProfile`은
canonical 관계 predicate, 방향, 앵커와 표현 설정을 소유합니다. 관계가 존재하는
동안 부모 변경과 임시 물리 표현은 `OntologyAttachmentAdapter`만 담당합니다.

전투 Presenter는 선택적인 VFX 메타데이터만 소유합니다. 무기를 직접 붙이거나
Collider를 끄거나 별도의 장착 상태 fallback을 보유하면 안 됩니다. 장착
Adapter가 표현 Transform을 소유하는 동안 영속 투영은 배치 Transform을 쓰지
않습니다.

## 온톨로지 표현

- `attachment_relation_predicate`
- `attachment_relation_direction`
- `ItemToActor`
- `ActorToItem`
- 첫 오른손 무기 계약: `Actor equipped_item Item`

메시명, 프리팹명, 인스턴스명은 장착 판단에 참여하지 않습니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 활성화 / 추가 | 임의 이름의 아이템도 Actor-to-Item 프로필이면 장착되고 물리와 Transform 소유권이 장착 Adapter로 이동 | `ActorToItemRelationUsesGenericAttachmentAndPhysicsOwnership` |
| 비활성화 / 제거 | 관계 제거 시 분리되고 월드 Transform 및 물리 소유권 복원 | 같은 PlayMode 테스트와 `DurableProjection_YieldsAndRestoresOwnershipForAttachmentPresentation` |
| 회귀 / 예외 | 전투 Presenter의 직접 장착 fallback이 없고 일반 배치 오브젝트는 계속 투영 Transform 적용 | `RightHandWeaponUsesGenericDataDefinedAttachmentContract` 및 기존 투영 테스트 |

## 성능과 멀티플레이 영향

투영 적용은 엔티티/의미를 먼저 확정한 뒤 영속 Transform을 적용하는 2단계가
됩니다. 장착 Adapter 동기화는 받은 투영당 한 번 실행하며 프레임별 DB 기록이나
새 polling을 만들지 않습니다.

## 갱신한 문서

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] 온톨로지 언어팩 CSV
- [x] 관련 테스트
