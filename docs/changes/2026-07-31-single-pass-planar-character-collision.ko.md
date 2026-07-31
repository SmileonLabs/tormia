# 단일 패스 평면 캐릭터 충돌

## 요약

- **날짜:** 2026-07-31
- **영역:** 로컬 플레이어 충돌 표현
- **상태:** 구현 및 검증 완료
- **문제:** 위쪽 이동을 요청하지 않았는데도 낮은 폴리곤 지형, 오브젝트
  가장자리와 물가 경계에서 아바타가 위로 튈 수 있었습니다.

## 결정

지지면 탐지는 관측 전용으로 유지합니다. 수평 입력은 세계 평면을 유지하고,
작성된 중력·점프 속도·수영 변위·승인된 컨트롤러 임펄스와 한 번의
`CharacterController.Move`에 합칩니다. 프로젝트 코드는 수평 입력을 탐지한
지지면 삼각형 법선에 먼저 투영한 뒤 CharacterController에 같은 충돌을 다시
해결시키지 않습니다.

기본 단차 기능은 온톨로지 충돌 역할을 구분할 수 없으므로 역할 기반 명시적
단차 판정은 유지합니다. 현재 아바타를 지지하는 동일 Collider의 면은 연속
지형이며 자기 자신을 단차로 사용할 수 없습니다. 별도의
`WalkableSupport` Collider만 명시적 단차 상단이 될 수 있습니다.

## 온톨로지 경계

Triple, Rule Block, Physical Meaning 또는 영속 월드 데이터는 변경하지
않았습니다. `LocalCharacterController`가 계속 표현 lease를 부여합니다.
지지면 법선·충돌 플래그·진단 추적은 일시적인 관측값입니다. Physical
Meaning을 제거하면 로컬 이동도 계속 제거됩니다.

## 증거

- `WalkableSlopeDoesNotInjectVerticalDisplacement`
- `CurrentSupportColliderCannotBecomeItsOwnStepObstacle`
- `GroundAdhesionOnWalkableSlopeDoesNotCreatePlanarDrift`
- `ActorBodyCannotBeUsedAsACharacterStep`

하이브리드 캐릭터 이동 PlayMode fixture 9/9가 통과했습니다. 개발 진단은
요청하지 않은 상승이나 외부 Transform 쓰기가 있을 때만
`[PlayerMotionTrace]`를 출력하며 Fact나 네트워크 메시지를 만들지 않습니다.
