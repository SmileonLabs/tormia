# Authority UDP 실시간 리스너

## 결정

이동 데이터그램을 인증하고 차단 경계를 적용한 뒤 격리된 제한 의도 큐로만
전달하는 기본 비활성 LiteNetLib 리스너를 추가한다. Authority 이동 통합 단계
전에는 이 큐를 게임 동작에 연결하지 않는다.

## 경계

- HTTPS가 티켓 발급을, SignalR/HTTP가 대체와 복구를 소유한다.
- UDP는 온톨로지 규칙, 게임 결과, 영속 Fact, 사용자 신원을 소유하지 않는다.
- 정확한 현재 세대, HMAC, 재전송 검증은 닫힌 상태로 실패한다.
- 이동 의도는 `Unreliable` 채널 0만 허용한다.

## 증거

- `AuthorityUdpRealtimeListenerTests`: 프로토콜, 증명, 재전송, 세대, 용량 검증.
- `AuthorityUdpRealtimeListenerIntegrationTests`: 실제 localhost LiteNetLib
  핸드셰이크, 전송 필터, 잘못된 요청·속도 제한 거절, 종료 검증.
- 2026-08-04 서버 전체 테스트 212개 통과, 실패 0개.
