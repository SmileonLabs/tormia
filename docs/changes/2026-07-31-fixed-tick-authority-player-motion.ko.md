# 고정 Tick Authority 플레이어 이동

날짜: 2026-07-31

## 결정

공유 플레이어 이동은 canonical 입력, 평가된 런타임 행동 발생, 작성된 이동
튜닝, Zone 경계, 작성된 충돌 프록시를 사용해 World Authority에서 50ms 고정
Tick으로 전진합니다. Unity가 충돌을 해결한 Pose는 순서가 있는 일시 관찰이며
Authority 상태를 덮어쓰지 않습니다.

## 생산 라인

`입력 트리거 -> canonical 일시 입력/행동 -> 작성된 트리플과 배정 Rule Block
-> 평가된 행동 발생 -> Authority 고정 Tick 적분 -> 작성된 충돌 프록시/Zone
해결 -> 공유 이동 투영 -> Unity 예측과 표현`

## 활성 경로 증거

- 방향을 정규화하고 요청 속도를 `movement_speed`로 제한합니다.
- 플레이어 Capsule 전체가 Zone 안에 머물고 작성된 `DynamicProp`을 통과하지
  못합니다.
- 작성된 `jump_action`과 일치하는 승인 발생이 작성된 도약 속도·중력·지면
  고정 속도를 사용하며 한 번 착지합니다.

## 제거·변조 경로 증거

- 필수 이동·점프·충돌 의미를 제거하면 fail-closed로 동작합니다.
- 오래된 Pose 관찰은 거절합니다.
- 클라이언트 Pose 관찰은 Authority 이동을 덮어쓸 수 없습니다.

## 다음 단계로 미룬 경계

서버 `WalkableSupport` 높이·높이장 해석과 부드러운 로컬 예측 보정은 후속
단계입니다. 현재 수직 지면 기준은 영속 체크포인트 Y이므로 불완전한 지지면
모델을 기준으로 로컬 Transform을 보정하지 않습니다.
