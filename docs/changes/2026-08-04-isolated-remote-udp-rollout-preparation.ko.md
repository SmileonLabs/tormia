# 분리된 원격 UDP 롤아웃 준비

## 요약

- **날짜:** 2026-08-04
- **담당:** TOV 팀
- **상태:** 준비 완료, 원격 업로드와 전환은 명시적 승인 필요
- **범위:** `tormia-test-authority`와 `/opt/tormia-test`만

## 확인한 토폴로지

현재 AWS 테스트 Authority는 Docker 컨테이너 하나로 실행되며 TCP 8080은
`127.0.0.1:5272`에만 공개된다. 같은 호스트의 Nginx가
`tov-api.punkarena.app` HTTPS를 종료하고 이 loopback 포트로 전달한다. 컨테이너는
기본 Docker bridge를 사용하며 확인된 gateway는 `172.17.0.1`이다. UDP 5273은
아직 매핑되거나 listen하지 않는다. UFW는 비활성이다. 전환 전 공개 `/health`,
`/health/realtime`, `/health/scheduler`는 모두 HTTP 200을 반환했다.

## 준비한 롤아웃

- 불변 후보 이미지는 `tormia-world-authority:test-20260804-udp-stage14`이고
  내보낸 archive SHA-256은
  `1ed9a6f9c5d4c070c90b80d51a4bc82c284d030e248393b11af774b94d01a86d`다.
- `configure-remote-test-udp-runtime.sh`는 mode 0600의 안정적인 32바이트 자격
  증명 보호 키를 한 번 만들며 유효한 키 재료를 출력하거나 자동 회전하지 않는다.
- `run-remote-test-authority-udp.sh`는 정확한 Docker bridge gateway를 계산하고
  host UDP 매핑이 없는 후보를 loopback TCP 5274에서 검증한다.
  `/health/realtime`이 Redis, ticket, listener 준비를 모두 증명해야 승격한다.
- 이전 컨테이너는 `tormia-test-authority-rollback-stage13`으로 보존한다. 승격
  실패 시 자동 복구하고 별도 rollback helper는 실패한 UDP 컨테이너도 진단용으로
  보존한다.
- 최종 승격은 `127.0.0.1:5272:8080/tcp`와 `5273:5273/udp`만 매핑한다.
  HTTPS와 SignalR은 계속 신뢰성 있는 제어·복구 경로다.

## 보안 경계와 차단 사항

저장소의 서버 메모에는 EC2 보안 그룹이 모든 트래픽을 허용한다고 적혀 있지만,
AWS 자격 증명이 없어 실제 규칙을 권한 있게 감사하거나 좁힐 수 없었다. 따라서
배포 시 인증된 외부 UDP 클라이언트 probe를 도달성 기준으로 사용하고 보안 그룹이
좁혀졌다고 주장해서는 안 된다. 외부 전송에는 명시적 사용자 승인이 필요하므로
원격 artifact 업로드와 전환은 아직 수행하지 않았다.
