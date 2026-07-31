# 플레이어 접지와 Authority 정지 후 위치 수렴

## 요약

- **날짜:** 2026-07-29
- **담당:** 프로젝트 소유자와 Codex
- **상태:** 트랜잭션형 입장 준비 계약으로 대체됨
- **범위:** Unity 월드 입장 접지와 로컬 플레이어 이동 표현

## 의도

월드 입장 후 첫발에서 아래로 꺼지는 현상과, 낮은 빈도의 Authority 위치가
반응성 있는 로컬 이동을 반대로 당겨 반복적으로 끊겨 보이는 현상을 제거합니다.

## 데이터 분류

- Unity 표현
- 일시적 런타임 전송 상태

계정 프로필, 영속 월드 Fact, 추론 결과, 룰블록, 물리 프로필은 추가하거나
변경하지 않습니다.

## 결정과 경계

체크포인트 복원이 영속 아바타 Transform을 계속 소유합니다. 이전의 비활성
아바타/`OnEnable` 접지 경로는 제거했습니다. 현재 계약은 아바타 루트를 활성
상태로 유지하되 숨김·입력 차단하고, 명시적인 월드 입장 표현 트랜잭션 안에서
충돌 접지를 완료합니다.

입력 어댑터는 계속 `CharacterController.Move`의 유일한 소유자입니다. 로컬
이동 입력 중이거나 Authority가 아직 `moving`을 보고하는 동안에는 위치
보정값을 만들지 않습니다. 입력이 멈추고 Authority가 `idle`을 보고하며 설정된
안정 대기 시간이 지난 뒤에만 같은 컨트롤러 이동에 제한된 수평 보정값을
합칩니다. Unity는 전투·장착 거리를 늘리지 않으며 이 표현 보정을 영속
명령으로 발행하지 않습니다.

## 온톨로지 표현

기존 Authority 이동 계약의 표현 타이밍 변경입니다. 새 canonical 온톨로지
용어나 숨은 게임플레이 권한을 만들지 않습니다. 작성된 `movement_speed`가
계속 서버 소유 최대값입니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 활성화 | 입장 준비가 접지를 완료한 뒤 `InWorld` 전환이 입력을 엶 | `WorldEntryPresentationPreparesBeforeSessionActivation` |
| 활성화 | 정지 Authority 보정은 수평이며 제한됨 | `AuthorityCorrectionIsHorizontalAndBounded` |
| 비활성화 | 로컬 입력 중이거나 Authority가 `moving`이면 보정 없음 | `AuthorityCorrectionWaitsForLocalStopAndIdleAuthority` |
| 회귀 | 첫 이동이 지지면 높이를 유지함 | `EntryGroundingKeepsFirstMoveOnSupportSurface` |

전체 Unity 검증 결과는 EditMode 174/174, PlayMode 47/47입니다.

## 성능과 멀티플레이 영향

새 polling, DB 쓰기, 영속 이벤트, 프레임별 할당 경로를 추가하지 않습니다.
기존 Authority 이동 조회 주기는 그대로 유지합니다. 입력 중에는 보정을
중지하고 안정된 정지 샘플에서만 다시 시작합니다.

## 갱신한 문서

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
