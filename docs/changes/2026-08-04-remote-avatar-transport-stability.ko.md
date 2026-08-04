# 원격 아바타 전송 안정화

## 결정

- 일반 REST 요청과 SignalR WebSocket 업그레이드를 Nginx 경로로 분리한다.
  일반 Authority API 요청에는 더 이상 `Connection: upgrade`를 강제로 넣지 않는다.
- 연속으로 발생하는 `zoneRuntimeChanged` 알림은 알림마다 HTTP를 호출하지 않고
  제한된 한 번의 원격 아바타 스냅샷 갱신으로 합친다.
- 하나의 Authority 아바타는 하나의 활성 조작 세션만 가진다. 에디터와 모바일
  멀티플레이 테스트는 서로 다른 계정과 아바타를 사용한다.

## 증거

- 프록시 분리 전 고빈도 HTTPS 상태 확인에서 빈 TLS 응답이 간헐적으로 발생했다.
  배포 후 100회 모두 HTTP 200을 반환했다.
- 에디터와 모바일이 같은 test2 아바타를 사용할 때 런타임 로그에서
  `stale_player_intent`와 `player_runtime_session_mismatch`가 반복됐다. 에디터
  세션은 test1로 복구했다.
- 원격 스냅샷 알림 병합 PlayMode 테스트가 통과했다.

## 온톨로지와 Authority 경계

이번 변경은 위치 권한을 Unity로 옮기지 않는다. 입력은 임시 Intent이고 서버
이동이 최종 권한을 유지하며, Unity는 런타임 스냅샷을 읽고 보간만 한다.

