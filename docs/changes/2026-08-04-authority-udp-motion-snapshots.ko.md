# Authority UDP 이동 스냅샷

## 결정

교체 가능한 UDP Zone 이동 스냅샷은 canonical Authority 고정 Tick 프레임에서만
발행한다. HTTP와 SignalR은 완전한 제어·대체·복구 경로로 유지한다.

## 경계

- UDP는 Rule 평가, 게임 상태 또는 영속성을 소유하지 않는다.
- SignalR과 UDP는 하나의 원본 `FrameOccurrenceId`를 공유한다. Redis는 UDP
  backplane일 뿐이며 프레임 식별자나 분산 scalar Tick을 다시 쓰지 않는다.
  Redis 장애는 Authority 시뮬레이션이나 SignalR을 중단할 수 없다.
- 스냅샷 입력, 보관 바이트, 항목 수, 데이터그램 크기, 수신자, 전송 세대 조회를
  모두 제한한다.
- 승인된 변경 actor occurrence는 FIFO로 유지한다. 항목 수 또는 바이트 한도가 찬
  큐는 가장 새로 들어온 occurrence를 버리고 앞서 대기 중인 Zone delta를 대체하지
  않는다.
- Unreliable 채널 0 전송 전에 정확한 전송 세대, 로컬 세션, HMAC, 서버 packet
  sequence를 확인한다.

## 증거

- `AuthorityUdpMotionSnapshotIntegrationTests`가 인증된 페이지 분할, occurrence
  재생 차단, FIFO delta 보존, 포화 시 drop-newest를 검증한다.
- 전체 `Tormia.WorldAuthority.Tests` 254/254가 .NET 8 Docker에서 통과했다.
- Docker 서버 publish 빌드가 성공했다.

## 제거·실패 경로

UDP 비활성화, 오래된 전송 세대, peer 없음, 잘못되거나 너무 큰 프레임,
backplane 장애, Redis 시간 초과, 동일 occurrence 재생, 큐 포화는 UDP에서만
fail-closed 처리하며 canonical Authority와 SignalR 경로는 계속 동작한다.
