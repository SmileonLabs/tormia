# 월드 경사면과 공중 애니메이션 표현 계약

## 요약

- **날짜:** 2026-07-31
- **상태:** 구현 및 검증 완료
- **범위:** Unity 표현과 씬에 작성된 충돌 의미

## 의도

작성된 보행 가능 경사면에서는 로컬 플레이어가 접지 이동 상태를 유지하고,
실제 충돌 기반 착지가 시작되기 전까지 공중 애니메이션 수명 주기가 유지되게
합니다.

## 소유권과 경계

- 정적 씬 Collider는 모양, 이름, Rigidbody 부재만으로 게임플레이 의미를
  얻지 않습니다. Tormia 월드 씬의 단단한 환경 Collider에는
  `WalkableSupport`, 물 Collider에는 `WaterVolume`을
  `OntologyCollisionRoleAdapter`와 프로젝트 소유 Physical Profile Database로
  명시 배정했습니다.
- 로컬 플레이어의 일시적 `JumpStart`, `Airborne`/`Fall`, `Landing` 표현
  단계는 계속 `OntologyAnimationStateResolver`가 소유합니다. 투영된 일반
  `animation_intent` Fact가 실제 접지 착지 전에 이 물리 단계를 Idle로
  교체할 수 없습니다.
- 접지 Idle, 이동, 장비 상태에서는 투영된 의미 애니메이션 의도를 계속
  사용할 수 있습니다. Unity 입력이 게임플레이 규칙을 소유하지 않으며
  프레임별 영속 Fact도 만들지 않습니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 씬 충돌 의미 작성 | 모든 환경 Collider가 `WalkableSupport` 또는 `WaterVolume`으로 명시 해석됨 | `OntologyWorldCollisionSemanticAssetTests.WorldEnvironmentCollidersHaveExplicitOntologyRoles` |
| 공중 단계 우선권 | JumpStart, Airborne/Fall, Landing이 일반 투영 Fact에 의해 교체되지 않음 | `OntologyAnimationTransitionTests.PhysicalJumpPhaseOwnsBasePresentationOverProjectedFact` |
| 접지 의미 의도 | Idle, 이동, 장비 상태는 투영된 의미 애니메이션 의도를 계속 허용함 | `OntologyAnimationTransitionTests.GroundedBaseStateDoesNotSuppressProjectedSemanticIntent` |
| 경사·점프 회귀 | 보행 가능 경사면이 단차 상승이나 튕김을 만들지 않고 승인된 점프가 한 번 착지함 | `OntologyHybridCharacterMotionTests` 관련 PlayMode 테스트 |

## 제거 및 실패 경로

씬 충돌 역할 어댑터를 제거하면 해당 지지 의미도 제거되고 씬 자산 테스트가
실패합니다. MotionStateResolver 소유권을 끄면 공중 단계 우선권도 함께
제거되며, 숨은 의도 이름 예외는 남지 않습니다.

