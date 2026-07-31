# 단일 소유자 기반 충돌 해결 플레이어 이동

## 결정

플레이어 이동은 이제 `physical_profile LocalCharacterController`를
사용합니다. 물리 의미가 독점 Unity `motionDriver`와 `collisionRole`을
선언하고, 일반 이동의 `CharacterController.Move`는
`OntologyCharacterMotionCoordinator` 하나가 소유합니다. 입력·중력·수영·입장
접지·승인된 충격 표현은 이 경로에 변위만 제공합니다.

플레이어에 대한 기존 서버의 충돌 없는 X/Z 적분은 제거했습니다. Authority는
계속 이동·점프 액션과 배정된 Rule Block을 평가하고 소유권과 Zone 경계를
검증하며 순서가 맞는 런타임 샘플만 받습니다. Unity는 충돌 해결이 끝난 위치를
원격 표현용 임시 상태로 게시합니다. 위치 샘플은 월드 리비전을 올리거나 Fact를
기록하지 않습니다.

## 충돌 계약

- `WalkableSupport`만 현재 지지면과 명시적 단차 윗면이 될 수 있습니다.
- `ActorBody`, `DynamicProp`, 트리거, 물 영역은 단차가 될 수 없습니다.
- 내장 `CharacterController.stepOffset`은 항상 0입니다.
- 입장 접지는 실제 캡슐 반지름과 skin width를 사용하고 입력이 잠긴 동안 접촉을
  한 번 초기화합니다.
- 물리 의미를 제거하거나 변경하면 해당 이동 소유권과 어댑터가 비활성화되며
  컴포넌트 이름·프리팹 기반 대체 경로를 남기지 않습니다.

## 마이그레이션

- 플레이어 아바타 의미 계약: 버전 7
- 개발 콘텐츠 패키지: `social_village` 버전 `3.9.0`
- `MovePlayerFromIntent`: 규칙 버전 2
- `JumpPlayerFromIntent`: 규칙 버전 3
- 기존 플레이어는 입장 시 `AuthorityKinematic`을
  `LocalCharacterController`로 교체하고 두 Rule Block 바인딩을 이관합니다.
- 자율 액터는 계속 `AuthorityKinematic`을 사용합니다.

## 증거

- `ActorBodyCannotBeUsedAsACharacterStep`
- `LocalCharacterPhysicalMeaningOwnsAndRemovesMotionLease`
- `EntryGroundingKeepsFirstMoveOnSupportSurface`
- `WorldEntryPresentationPreparesBeforeSessionActivation`
- `ResolvedPoseRegistryRejectsStalePoseSequence`
- `CollisionResolvedPoseMustRemainInsideAuthoredZone`

