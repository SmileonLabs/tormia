# 점프 표현 생명 주기 소유자

## 결정

World Authority는 계속 점프 허용과 승인된 canonical 애니메이션 의도의
출처입니다. Manifest 기반 직접 캐릭터 이동을 사용하는 로컬 아바타에서는 충돌
관찰 기반 애니메이션 상태 해석기만 `JumpStart` → `Airborne`/`Fall` →
`Landing` 런타임 생명 주기를 소유합니다.

## 이유

일반 Authority 임시 표현기가 `JumpStart`를 비중단 일회성 클립으로 취급해,
CharacterController 충돌과 접지가 정상이어도 물리 상태 해석기가 관찰된 정점과
착지에서 표현을 바꾸지 못했습니다.

콘텐츠 Manifest도 원본 클립 이름이 같다는 이유로 canonical 공중 하강 `Fall`을
바닥 쓰러짐 반응에 배정하고 있었습니다. 직접 이동 `Fall`은 이제 제자리 공중
클립을 계속 사용하고, 쓰러짐 클립은 반응 의도만 유지합니다.

## 증거

- `AuthorityJumpPresentationUsesPhysicalStateResolverOwnership`
- `DescendingAvatarCannotRemainInJumpStart`
- 점프 Rule Block을 제거하면 점프 발생도 사라지며 공격·장착 액션은 계속 일반
  Authority 임시 표현을 사용합니다.
