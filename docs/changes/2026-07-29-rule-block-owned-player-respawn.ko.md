# Rule Block이 소유하는 플레이어 전투 리스폰

## 요약

- 날짜: 2026-07-29
- 영역: 영속 아바타 온톨로지, World Authority 액션 평가, Unity 체크포인트 표현
- 상태: 구현 및 검증 완료

## 의도

죽은 플레이어를 다른 게임플레이와 같은 재사용 가능한
트리플 -> Rule Block -> World Authority -> 물리 의미/표현 계약으로
복구하고, Unity가 소유하는 숨은 부활 fallback은 만들지 않습니다.

## 데이터 분류

- 영속 월드 데이터: `respawn_action`, `current_health`, `maximum_health`,
  `is_alive`, 배정 Rule Block, 의미 계약 마커
- 일시적 런타임 상태: 입력 요청, 접지 초기화, 이동 보정
- Unity 표현: Authority 전환 승인 뒤 이미 확정된 체크포인트 위치 적용

## 완전한 경로

플레이어 아바타가 `respawn_action -> respawn_avatar`를 작성하고
`RespawnPlayerOnDeath` Rule Block을 가집니다. 불변 액션이 이 블록을
호출합니다. Authority는 사망했고 피해를 받을 수 있는 플레이어 제어 액터인지
확인하고, `maximum_health`로 `current_health`를 회복한 뒤
`is_alive=true`를 설정합니다. Unity는 승인된 사영을 다시 읽고 나서만
확정 체크포인트 위치를 표현합니다.

## 제거 경로

Rule Block 배정을 제거하면 Authority가 액션을 거절합니다. Unity는 체력,
생존 상태, 위치를 복구하지 않습니다. 의미 계약 버전 1은 한 번만 도입하는
마커이므로 일반 갱신이 사용자가 제거한 행동을 몰래 되살리지 않습니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 활성 | 죽은 아바타가 최대 체력과 생존 사영으로 돌아온 뒤 확정 체크포인트로 이동 | 서버 평가기 테스트, Unity EditMode 계약 테스트, MCP 라이브 사영 |
| 제거 | Authority가 액션을 거절하고 Unity가 체크포인트 fallback을 실행하지 않음 | 서버 제거 테스트와 Unity 배정 테스트 |
| 라이브 | 기존 사망 아바타가 `RespawnPlayerOnDeath` 배정 상태에서 `current_health=100`, `is_alive=true`로 복구 | Unity MCP 라이브 프로브 |

개발 액션 패키지는 `social_village@3.2.1`입니다.
전체 Unity EditMode 회귀 테스트는 206/206으로 통과했습니다. World
Authority 테스트 프로젝트가 성공 종료했고,
`verify-development.ps1 -RequireServices -RequireUnityMcp`가 한·영 컨텍스트,
회귀 시나리오 목록, 서버 빌드, Docker 서비스, Authority 상태, 영속 Unity
MCP 연결 검증을 모두 통과했습니다.

## 갱신 문서

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
