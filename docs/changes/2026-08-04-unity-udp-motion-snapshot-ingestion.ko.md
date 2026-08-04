# Unity UDP 이동 스냅샷 수신

## 결정

UDP 입력 Writer를 활성화하지 않고 인증된 프로토콜 v2 이동 페이지를 기존의
전송 중립 Authority 이동 feed로 수신한다.

## 경계

- 부트스트랩 frame 버전 1과 realtime wire 버전 2를 구분한다.
- 제한 안에서 완성된 페이지 집합만 발행한다.
- SignalR/HTTP가 런타임 세션 장벽을 만들고 UDP는 이미 확인된 actor 세션만
  전진시킨다.
- `FrameOccurrenceId`로 경로 중복을 제거하고 actor별 Authority Tick으로 상태
  순서를 정한다. Unity 전송은 게임 상태나 Transform을 직접 바꾸지 않는다.

## 검증

- 개발 하네스 통과.
- Unity 스크립트 검증과 재컴파일에서 새로운 C# 오류 없음.
- 복구 장벽 보완 뒤 독립 감사 P0/P1 없음.
- MCP 테스트 실행기가 이전 진행률 0의 `tests_running` 작업에 멈춰 PlayMode 실행은
  보류 상태다. 이를 통과로 기록하지 않는다.

## 비활성·실패 경로

기본 비활성 수신기, 잘못된 인증·결합, 재전송, 누락·불일치 페이지, 확인되지 않은
actor 세션, 오래된 HTTP 복구, 비활성화, scope 변경은 UDP frame을 발행하지 않는다.
SignalR과 HTTP는 계속 사용할 수 있다.
