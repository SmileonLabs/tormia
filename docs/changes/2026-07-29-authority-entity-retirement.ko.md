# Authority 엔티티 폐기 파이프라인

## 요약

- 날짜: 2026-07-29
- 상태: 구현
- 범위: 영속 월드 데이터, Authority 전송, Unity 표현

## 의도

Unity 표현을 지운 것을 영속 월드 엔티티 삭제로 간주하지 않습니다.
처치와 폐기는 서로 다른 생명주기 상태입니다.

## 결정과 경계

- revision 기반 멱등 World Authority 명령 `retire_entity`를 추가합니다.
- 같은 revision에서 엔티티를 소프트 삭제하고 해당 엔티티를 설명하거나
  참조하는 활성 트리플, Rule Block, 의미 패키지 적용을 종료합니다.
- 과거 데이터와 명령·이벤트 증거는 보존합니다.
- 계정 소유 플레이어 아바타의 일반 폐기를 거부합니다.
- 영속 트랜잭션이 커밋된 뒤 자율 행동 임시 상태를 제거합니다.
- Unity는 승인된 Projection에서 엔티티가 빠진 뒤에만 표현을 제거합니다.
  Authority가 소유하지 않은 미리보기만 로컬 삭제합니다.
- `is_alive=false`, 템플릿, 프리팹, 메시, 이름으로 폐기를 추론하지
  않습니다. 향후 자동 정리는 명시적인 생명주기 Rule Block·액션이어야
  합니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| Authority 폐기 | 엔티티와 활성 의미 기여가 Projection에서 제외됨 | `scripts/run-entity-retirement-authority-smoke.ps1` |
| 멱등 재실행 | 승인 명령 ID 재실행이 원래 결과를 반환함 | Authority 스모크 |
| 이미 폐기됨 | 새 명령 ID로 없는 엔티티를 다시 폐기할 수 없음 | Authority 스모크 |
| Unity 표현 | Authority가 맡은 요청은 Projection 제거를 기다림 | `DeleteSelectionUsesAuthorityRetirementWhenClaimed` |
| 로컬 미리보기 | 맡지 않은 삭제는 로컬 미리보기만 제거함 | `DeleteSelectionFallsBackOnlyWhenNoAuthorityHandlerClaimsIt` |

## 성능과 멀티플레이

폐기는 프레임 단위가 아닌 한 번의 작성 트랜잭션입니다. 월드 잠금,
예상 revision, 명령 ID, Authority Projection이 모든 클라이언트에 동일한
영속 결과를 제공합니다. 폐기 엔티티의 Redis·메모리 자율 이동 상태는 DB
커밋 뒤에 제거합니다.

## 갱신 문서

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
