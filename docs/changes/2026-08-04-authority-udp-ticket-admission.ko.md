# Authority UDP 티켓 입장

## 결정

UDP 전송 입장은 인증된 HTTPS 제어 경로에서 시작한다. Authority는 계정이
소유하고 활성 런타임 세션이 있는 아바타에만 짧은 수명의 불투명 ticket과
별도 데이터그램 키를 발급한다. 전송 generation은 서버가 배정하며 클라이언트는
선택할 수 없다.

ticket 사용에는 nonce와 domain-separated HMAC 소유 증명이 필요하다. 잘못된
증명은 ticket을 소모하지 않는다. Redis는 현재 런타임 세션과 Zone을 다시
확인하고 정확한 ticket을 한 번만 소모하며 짧은 인증 전송 handshake를 원자적으로
승격한다. 데이터그램 키는 명시적 key ID와 함께 AES-GCM으로 보호해 저장한다.

기능은 기본 비활성이다. 운영 입장에는 Redis, 자격 증명 보호, 명시적으로 신뢰한
프록시를 통한 HTTPS, 멀티인스턴스 저장소가 모두 필요하다. 비활성화 시 대기
ticket, generation 상태, 승격된 전송 세션을 철회한다. 이 전송 상태는 일시적이며
Triple, Rule Block, 게임 결과, 영속 월드 이벤트를 작성하지 않는다.

## 남은 활성화 조건

이 변경은 UDP 소켓을 활성화하지 않는다. 수신기가 제한된 `RejectForce`, payload
해석 전 인증, 재전송 방지 window, 단일 writer 소유권을 증명해야 이동을 UDP로
보낼 수 있다.

