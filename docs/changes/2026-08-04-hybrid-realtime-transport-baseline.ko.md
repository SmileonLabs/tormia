# 하이브리드 실시간 전송 기준선

## 결정

UDP를 추가하기 전에 기존 HTTPS/SignalR Authority 이동 경로를 계측한다. UDP는
교체 가능한 고빈도 전송 수단이며 의미 권한, 영속 상태, 전투 결과, 생명주기와
presence 복구를 소유하지 않는다.

## 확인된 기준선

- Unity는 명목상 이동 intent 15Hz, 충돌 해결 pose 관측과 로컬 Authority 조회를
  10Hz로 전송한다.
- Authority는 목표 20Hz 고정 Tick으로 플레이어 이동을 계산하고 변경된 이동
  상태를 SignalR로 발행한다.
- 기존에는 재접속 원인, snapshot age, 역순, buffer underrun, Tick 초과와 발행
  지연을 확인할 운영 증거가 없었다.

## 추가한 증거

- 서버 `RealtimeTransportMetrics`가 낮은 카디널리티 태그로 Tick과 SignalR 발행
  상태를 기록한다.
- Unity `OntologyRemoteMotionRuntimeMetrics`가 Fact나 규칙을 변경하지 않고 현재
  세션의 전송·보간 상태를 노출한다.
- 인증, 손실, 역순, failover, Android/PC와 외부망 검증 전까지 UDP는 비활성이다.
