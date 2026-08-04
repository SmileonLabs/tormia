# 하이브리드 UDP 전송 기술 선정

## 결정

TOV의 첫 UDP 이동 수직 슬라이스는 LiteNetLib 2.1.4를 사용한다. 서버는
정확한 NuGet 버전을, Unity는 정확한 OpenUPM 패키지 버전을 고정한다.
OpenUPM 자산은 upstream revision
`3bfa89948f6fa4e00c421f75cbd999e048f493d8`과 registry SHA-512 무결성 값
`lLx3T31CW665adCUOaD6jqxARbjumqeM/29fnZ5091bPrbghjRFJgC4kDQMumgy7Sl+4zbZjpWi7MPR886aVXw==`으로 추적한다.

LiteNetLib는 데이터그램 전달 수단일 뿐이다. 사용자 신원, 게임 권한,
이동 규칙, Authority 상태, Projection 복구, 영속 데이터의 소유자가 아니다.
HTTPS와 SignalR은 인증된 제어·복구 경로로 계속 유지한다.

## 보류한 대안

- Unity Transport는 현재 Authority가 Unity headless 서버가 아닌 ASP.NET Core
  .NET 8 서비스이므로 보류한다.
- QUIC은 .NET 8의 `System.Net.Quic`이 preview이고 Unity Android IL2CPP에서
  검증되지 않은 native 의존성이 추가되므로 보류한다.

## 활성화 조건

다음을 모두 통과하기 전에는 UDP 경로를 활성화하지 않는다.

- 고정 패키지가 Unity Editor, Android IL2CPP, Linux .NET 8에서 컴파일된다.
- HTTPS가 짧은 수명의 일회용 전송 ticket을 발급하며 bearer token을 UDP에
  노출하지 않는다.
- 데이터그램은 인증·generation 결합·재전송 방지 검사를 거치며 잘못된
  패킷은 payload 할당 전에 거절된다.
- 손실·역순·재연결·UDP 차단 fallback·오래된 세션 테스트가 기존 Authority
  이동 계약을 보존한다.
- 외부 테스트 배포는 기존 HTTPS 가상 호스트에 영향을 주지 않는 별도 UDP
  포트를 사용한다.
