# Authority UDP 결정론적 장애 보강

날짜: 2026-08-04

## 결정

12단계는 전송 장애를 게임 권한 밖에 유지하고 기존 UDP 경로를 결정론적 서버
테스트로 검증한다. production 코드는 제한된 consumer drain 경계, 정확하고
카디널리티가 낮은 진단, 원자적 만료 티켓 소비, 설정 가능한 제한 Redis
generation fence만 추가한다.

검증하는 전체 경로는 다음과 같다.

`인증 데이터그램 -> replay/generation fence -> 제한된 consumer -> canonical
이동 승인 -> writer/revision fence -> 일시적 intent registry`

## 장애 정책

- 손실은 없는 sequence를 생성하지 않는다.
- 역순 패킷은 replay window 안에서 전달될 수 있지만 registry를 되감지 않는다.
- replay와 너무 오래된 패킷은 canonical 승인에 들어가지 않는다.
- fallback 또는 generation 철회 전에 대기한 패킷은 나중에 commit되지 않는다.
- 만료 티켓의 첫 사용은 자격 증명을 소비하고 `Expired`를 반환하며 이후 사용은
  `AlreadyRedeemed`를 반환한다.
- 스냅샷 페이지, 제한된 drop, backplane 장애는 교체 가능한 표현 동작으로
  유지하며 기존 snapshot 통합 테스트가 검증한다.

## Redis 통합 정책

Redis Lua 통합 테스트는 `TORMIA_TEST_REDIS_CONNECTION`을 지정한 경우에만
실행한다. 각 테스트는 고유한 world, user, avatar, runtime, transport ID와
독립 연결 두 개를 만들며, 연결 문자열에 `defaultDatabase=1` 이상의 격리된
비기본 데이터베이스를 명시해야 한다. 자신이 생성한 정확한 키만 삭제하고 DB
전체 초기화와 관련 없는 키 열거는 금지한다.

## 증거

- `AuthorityUdpMotionIntentConsumerFaultTests`: 손실, 역순, replay, too-old,
  fallback 후 대기 패킷, 진단 reason 보존.
- `UdpTransportTicketStoreTests`: 인메모리 만료 티켓 정책.
- `RedisUdpTransportIntegrationTests`: 인스턴스 간 티켓 수명주기, 만료·replay
  parity, 동시 중복 교환, 정확한 generation 다중 chunk 조회, 재발급 대 승격
  generation fence, 제출 대 fallback 최종 writer fence.
- `UdpTransportTicketStoreTests.RedisGenerationLookupChunking_BoundsEveryLuaInvocation`:
  10,001개 binding 요청도 설정된 64개 상한보다 큰 Lua chunk를 만들지 않는다.
- `AuthorityUdpMotionSnapshotIntegrationTests`: 결정론적 페이지, 제한 큐·drop,
  backplane 장애 격리.
- `AuthorityUdpRealtimeListenerIntegrationTests`: 새 generation 재연결은
  성공하고 대체된 bootstrap은 데이터 경로 입력 전에 거절된다.

이 전송 테스트와 경계는 Triple, Rule 결과, 영속 이벤트, 이동 허용, Unity
Transform을 만들지 않는다.
