# 작성된 지지면과 평면 보정

날짜: 2026-07-31

## 결정

World Authority 접지는 영속 엔티티에 작성된 `WalkableSupport` Box 프록시
의미에서만 계산합니다. 기존 체크포인트 Y 지면 fallback은 제거합니다.
소유한 최신 서버 스냅샷은 제한된 X/Z 보정값을 만들 수 있지만, Unity는 이
보정을 유일한 `OntologyCharacterMotionCoordinator`
`CharacterController.Move` 호출 안에서 소비합니다.

## 소유권

- 영속 월드 데이터: 지지면 엔티티 Transform과 충돌/물리 Fact
- 작성 플레이어 데이터: 최대 단차 높이와 지면 여유 높이
- 일시적 Authority 상태: 접지 여부, 지지 엔티티 ID, 속도, 고정 Tick Pose
- Unity 표현: 로컬 충돌 예측과 제한된 평면 보간

수동적인 지지면 형상은 Trigger나 행동 결과가 없으므로 새 Rule Block이
적용되지 않습니다. 기존 이동과 점프 액션은 계속 배정된 Rule Block을
호출합니다.

## 제거 경로

지지면을 폐기하거나 역할/프록시 의미를 제거하면 Authority 접지도
제거됩니다. 체크포인트 Y, Unity Mesh, 오브젝트 이름, 정적 Collider, 직접
Transform 쓰기, 두 번째 `CharacterController.Move`는 이를 복구하지 않습니다.

## 검증

- 서버 정책 테스트로 작성된 여유 높이와 지지면 제거 동작을 검증합니다.
- EditMode 테스트로 결정적 지지면 ID와 제한/오래된 보정 동작을 검증합니다.
- PlayMode 테스트로 보정이 단일 코디네이터 Move까지 지연되고 그 안에서
  소비되는지 검증합니다.
