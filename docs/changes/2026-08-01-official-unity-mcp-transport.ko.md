# Unity 공식 MCP 전송

## 요약

- **날짜:** 2026-08-01
- **담당:** TOV 개발 인프라
- **상태:** 전환 검증
- **관련 요청:** 반복적으로 연결이 끊기는 서드파티 Unity MCP 전송 교체

## 의도

Unity 공식 Assistant MCP Relay를 Codex와 에디터 사이의 기본 전송으로
전환하되, 재시작 및 도구 범위 검증이 끝나기 전에는 기존 연결을 보존합니다.

## 결정과 경계

Codex는 Windows 공식 Relay를 `--mcp`로 실행하고 `--project-path`로 TOV
프로젝트를 고정합니다. Relay는 named pipe로 에디터 Bridge와 통신합니다.
기존 Streamable HTTP 엔드포인트는 검증 기간에만 임시 fallback으로 남기며,
공식 경로가 전환 기준을 통과하면 `com.coplaydev.unity-mcp`와 함께 제거합니다.

이 변경은 개발 인프라만 바꿉니다. 월드 Fact, Rule Block, 물리 의미, Authority
상태 또는 게임플레이 동작을 소유하거나 변경하지 않습니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 공식 전송 활성 | Relay, 프로젝트 고정 Codex 설정, 실행 중인 에디터 연결 레코드와 named pipe가 존재함 | `scripts/verify-unity-mcp.ps1` |
| 승인된 대화형 클라이언트 | MCP 초기화와 도구 검색에서 씬, GameObject, 콘솔 도구가 노출됨 | `scripts/verify-unity-mcp.ps1 -LiveToolProbe` 또는 재시작한 Codex 세션 |
| 설정 누락/오류 | Relay, Assistant 패키지, 프로젝트 고정, 에디터 Bridge 또는 필수 도구가 없으면 명시적으로 실패 | `scripts/verify-unity-mcp.ps1` 음성 검사 |
| 임시 fallback | 명시적으로 요청할 때만 기존 HTTP 상태를 보고하며 공식 검증을 대신하지 못함 | `-CheckLegacyFallback` |

## 제거 기준

Codex 재시작, Unity 재시작, 어셈블리 리로드, 콘솔 읽기, 씬 조회,
GameObject 변경과 실행 취소, 저장, 컴파일, Unity 테스트가 공식 도구를 통해
통과한 뒤에만 기존 HTTP MCP 패키지와 설정을 제거합니다.

## 갱신 문서

- `AGENTS.md`
- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `scripts/verify-unity-mcp.ps1`
- `scripts/verify-development.ps1`
