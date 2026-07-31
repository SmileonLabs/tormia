# 프리팹 소유 장착 손잡이 기준점

## 요약

- **날짜:** 2026-07-28
- **상태:** 구현 및 검증
- **범위:** Unity 장착 표현과 에디터 저작 도구

## 의도

공용 오른손 장착 프로필에서 특정 검 메시용 보정값을 제거합니다. 각 프로젝트
소유 무기 프리팹이 캐릭터 손 소켓에 맞닿아야 하는 정확한 손잡이 위치와 방향을
직접 선언합니다.

## 소유권 결정

- 장착 여부는 계속 Authority Fact가 결정합니다.
- 장착 프로필은 안정적인 배우 소켓 ID와 선택적인 소켓 조정만 소유합니다.
- `OntologyAttachmentSocket`은 프로젝트 소유 Actor 표현 데이터입니다. 외부
  캐릭터 프리팹을 수정하지 않고 명시적으로 지정한 리그 뼈 또는 소품 앵커를
  따라갑니다.
- 무기 프리팹의 `OntologyAttachmentGripPoint`가 모델별 손잡이 정렬을 소유합니다.
- 범용 장착 어댑터가 두 표현 계약을 정렬합니다.
- 게임플레이 코드는 무기 ID, 프리팹명, 메시명을 검사하지 않습니다.

## 활성·제거 경우

| 경우 | 기대 결과 |
| --- | --- |
| 프리팹에 손잡이 기준점이 있음 | 기준점의 위치와 회전이 배우 소켓과 일치 |
| 필수 무기 손잡이 기준점을 제거함 | 숨은 대체 처리 없이 무기 장착 표현 비활성화 |
| 선택형 기존 손잡이 기준점을 제거함 | 비무기 호환성을 위한 명시적 공용 프로필 자세 유지 |
| 작성된 배우 소켓이 있음 | Humanoid 손 뼈 축 대신 작성된 소켓에 아이템 장착 |
| 장착 관계를 제거함 | 월드 Transform과 물리 소유권이 아이템으로 복귀 |

## 저작 흐름

오른손 장착 프리뷰를 열고 `EquippedWeaponPreview`의 위치와 회전을 조정한 뒤
**Save Current Pose To Prefab Grip Point**를 누릅니다. 공용 프로필은 변경되지
않습니다.

배우 쪽 자세는
`OntologyPlayer/AttachmentSockets/RightHandWeaponSocket`에서 수정합니다.
Transform을 이동·회전한 뒤 **Capture Current Transform As Socket Pose**를
누릅니다. 런타임에서는 외부 리그의 `RightHand/RightHandProp`을 따라갑니다.

## 검증

- PlayMode 장착 테스트가 필수 기준점 존재·제거 경로와 선택형 기존 프로필 경로를
  모두 검증합니다.
- 실제 `TormiaWorld` 플레이어와 `Sword08Corrupted` 런타임 통합 검증에서
  `RightHandWeaponSocket`을 선택하고 위치·회전 오차 0으로 장착됨을 확인했습니다.
- 프로젝트 소유 검 프리팹 3개에 수정 가능한 손잡이 기준점 자식을 둡니다.
- 변경 뒤 개발 하네스와 Unity Console을 확인합니다.

## 갱신 문서

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
