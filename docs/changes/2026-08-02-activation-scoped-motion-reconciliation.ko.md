# 활성화 세션 단위 이동 보정

## 요약

- **날짜:** 2026-08-02
- **상태:** 구현 및 검증 완료
- **문제:** 늦게 도착한 Authority 상태가 멈춘 플레이어를 뒤로 끌 수 있었고, 이전 활성화의 실행 중 Tick이 새 런타임 상태를 덮을 수 있었습니다.

## 소유권과 결정

플레이어 입력과 충돌 해결 위치 표본은 일시적 관찰입니다. 공유 이동 상태는 Authority가 소유합니다. Unity는 로컬 표현을 예측할 수 있지만, 현재 런타임 세션에서 Authority가 마지막 승인 입력까지 처리한 경우에만 위치 보정을 소비합니다.

활성화마다 서버가 `RuntimeSessionId`를 생성합니다. 이동 Intent, 런타임 Action Intent, 위치 관찰, 이동 상태가 이 값을 공유합니다. scheduler 발행은 세션과 Tick을 함께 비교하는 CAS를 사용하고, Unity 보정은 세션·처리된 입력 sequence·Tick을 함께 검증합니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 이동 N 이후 정지 N+1 | 이전 잔여 보정을 즉시 제거 | Unity EditMode 이동 보정 테스트 |
| 새 Tick이지만 처리 sequence는 N | 상태 거절 | Unity EditMode 이동 보정 테스트 |
| 처리 sequence가 N+1 | 보정 허용 가능 | Unity EditMode 이동 보정 테스트 |
| 새 활성화와 이전 scheduler 경합 | 이전 세션 쓰기 거절 | `PlayerMotionRuntimeSessionTests` |
| 재접속 후 sequence 1 시작 | 새 세션에서 독립적으로 승인 | `PlayerMotionRuntimeSessionTests` |

## 성능 및 멀티플레이 영향

추가 검사는 고정 크기 식별자와 정수 비교뿐입니다. 프레임별 관찰을 영속 저장하지 않고 월드 Fact나 Rule 평가를 추가하지 않습니다.

## 갱신 문서

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
