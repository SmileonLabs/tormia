# 명시적 이동 writer 전환

## 결정

첫 UDP 패킷이 writer를 암묵적으로 승격하던 구조를 인증된 명시적·멱등
Authority 전환으로 교체한다. 런타임 활성화는 HTTP writer로 시작하며 티켓
발급과 교환만으로 writer가 변경되지 않는다.

## 서버 계약

- UDP 승격: `POST /v1/worlds/{worldId}/runtime/avatars/{avatarId}/transport/udp/promote`
- HTTP fallback: `POST /v1/worlds/{worldId}/runtime/avatars/{avatarId}/transport/http/fallback`
- 두 요청은 정확한 runtime session, 인증 transport session, generation,
  예상 writer epoch, 예상 월드 revision을 전달한다.
- HTTP 이동 intent는 `writerEpoch`를 포함한다. UDP는 정확한 활성 writer
  결합에서 epoch를 조회한다.
- Redis Lua와 동일한 인메모리 경계가 writer mode·epoch, runtime, revision,
  generation, 승격 결합, 오래된 intent lease를 원자적으로 차단한다.

## 티켓 재발급과 연결 presence 정책

UDP가 활성 상태이면 티켓 발급은 `udp_rekey_requires_fallback`으로 실패한다.
새 티켓을 요청하기 전에 HTTP로 명시적 fallback해야 하며 11단계에서는 직접
UDP-to-UDP writer 교체를 허용하지 않는다. 교환된 handshake 증거와 연결 peer
presence를 분리한다. 연결 종료는 presence만 철회하며 fallback은 정확한 현재
writer를 원자적으로 검증하고 generation fence를 전진시킨다. datagram 승인은
정확한 connected presence를 요구하므로 연결 종료나 fallback 뒤 대기 패킷은
실패 폐쇄된다.

fallback은 정확한 현재 runtime, binding, writer epoch를 검증한 뒤 현재
Authority revision을 사용한다. 따라서 UDP 승격 뒤 월드가 편집되어도 신뢰성
있는 복구가 영구 차단되지 않는다. 승격이 실패해 HTTP가 예상 epoch의 현재
writer로 남아 있으면 후보 전송 상태를 정리하고 변경 없는 Authority HTTP
writer를 반환하는 멱등 성공으로 처리한다.
멱등 성공과 거절 writer 증거는 world, user, avatar, Zone, runtime 범위가
정확히 일치해야 하며 HTTP 계층은 범위 확인 없는 writer로 결과를 덮어쓰지
않는다.

## 온톨로지 경계

상태 머신은 전송 메타데이터만 소유한다. 이동 허용은 계속 작성된 Triple,
배정된 Rule Block, Authority 평가와 고정 Tick 이동 런타임에서 나온다. 전환은
Fact나 게임 결과를 만들지 않는다.

## 검증

- Docker 서버 publish 빌드: 통과.
- World Authority 전체 테스트: 2026-08-04 기준 267개 통과, 실패 0개.
- 하네스 시나리오: `hybrid-udp-single-writer-transition`.
