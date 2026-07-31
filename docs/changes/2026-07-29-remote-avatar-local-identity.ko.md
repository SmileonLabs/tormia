# 원격 아바타의 로컬 식별자 제외

## 요약

- **날짜:** 2026-07-29
- **담당:** Codex
- **상태:** 구현 및 검증
- **관련 요청:** 익사 복구 후 Authority 모션이 갱신될 때 흰색 중복
  아바타가 나타나는 현상 방지

## 의도

범용 월드 엔티티가 먼저 발견됐다는 이유로 로컬 플레이어가 원격
아바타로 한 번 더 표시되면 안 됩니다.

## 데이터 분류

- Unity 표현: 로컬·원격 아바타 제외와 복제본 수명
- 런타임 관측: Authority 모션 스냅샷은 변경 없이 읽음
- 계정 프로필·영속 월드 데이터 변경 없음

## 결정과 경계

원격 표시기는 등록된 계정 입장 아바타 또는 로컬 플레이어 입력을
소유한 Actor에서만 로컬 제외 ID를 구합니다. 장비와 배치 오브젝트도
같은 컴포넌트를 사용하므로 범용 `OntologyAuthorityEntityIdentity`
탐색은 로컬 플레이어를 증명할 수 없습니다.

안정 로컬 식별자가 표시기보다 늦게 준비되면 해당 ID의 원격 복제본을
즉시 제거합니다. Authority 모션과 영속 엔티티는 변경하지 않습니다.

## 온톨로지 표현

새 트리플이나 룰블록은 추가하지 않습니다. 이는 표현 소유권 문제입니다.
등록·로컬 입력 Actor는 로컬 표현이며, 활성 Zone의 나머지 Authority
아바타만 원격으로 표시할 수 있습니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 활성화 | 로컬 입력 Actor 식별자가 잘못 캐시된 임의 월드 식별자를 교체 | `LocalAvatarResolutionIgnoresArbitraryWorldIdentity` |
| 제거 경로 | 해석된 로컬 아바타 ID와 같은 원격 복제본을 제거 | 런타임 MCP 검사에서 `AuthorityRemoteAvatar_Player Avatar` 없음 |
| 회귀 | 기존 PlayMode 동작 유지 | Unity PlayMode `50/50` |

## 성능과 멀티플레이 영향

기존 표시기 의존성 갱신 안에서 해석하며 DB 쓰기나 네트워크 요청을
추가하지 않습니다. 복제본 제거는 로컬 표현에만 영향을 줍니다.

## 갱신한 문서

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
