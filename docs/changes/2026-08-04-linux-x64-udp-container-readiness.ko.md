# Linux x64 UDP 컨테이너 준비

## 결정

World Authority 운영 이미지는 이제 `linux-x64` 대상으로 명시적으로
게시되고, 기반 이미지의 비루트 애플리케이션 사용자로 실행된다. TCP 8080과
UDP 5273은 이미지 메타데이터로 노출하며 로컬 TCP `/health` 검사도 포함한다.
UDP를 이미지 메타데이터에 노출하는 것은 호스트 포트 공개나 리스너 활성화를
의미하지 않는다. `Realtime:UdpEnabled`와
`Realtime:UdpListenerEnabled`의 기본값은 계속 false다.

명시적인 단일 인스턴스 Development 환경이 아니면 UDP 활성화는 실패 폐쇄
방식이다. Redis, 유효한 32바이트 자격 증명 보호 키와 키 ID, 비특권 리스너
포트가 모두 필요하다. 의존성 주입 생성도 Windows 개발 호스트뿐 아니라 Linux
Production 컨테이너에서 검증한다.

## 증거

- 깨끗한 다단계 Docker 빌드가 `UseAppHost=false`와 `linux-x64` 런타임으로
  Authority를 복원하고 게시했다.
- 생성된 `linux/amd64` 이미지는 uid/gid 1654로 실행되고 ASP.NET 8 기반
  이미지가 제공하는 .NET 네이티브 의존성을 정상적으로 해석했다.
- 기본 비활성 Production 컨테이너는 healthy 상태가 되고 두 UDP 플래그가
  false이며 호스트 UDP 매핑이 없다.
- Redis와 자격 증명 보호가 있는 로컬 검증 전용 Production 컨테이너는 두 UDP
  플래그가 true인 상태로 healthy가 되지만 호스트 UDP 포트는 매핑하지 않았다.
- Production UDP 보호 의존성이 빠지면 서버가 트래픽을 받기 전에 기동을
  중단한다.

## 14단계 경계

Compose, 클라우드, 방화벽, 로드 밸런서, DNS, AWS 배포는 변경하지 않았다.
14단계에서 실제 호스트 UDP 매핑을 명시적으로 추가하고 공개 프록시의 정확한
주소를 `Realtime:TrustedProxyAddresses`에 설정해야 한다. 광범위한 프록시
신뢰는 금지한다. HTTPS와 SignalR은 롤아웃 전체에서 신뢰성 있는 제어·복구
경로로 유지한다.
