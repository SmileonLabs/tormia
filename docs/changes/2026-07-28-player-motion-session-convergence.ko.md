# 플레이어 이동 세션 수렴

## 요약

- **날짜:** 2026-07-28
- **상태:** 구현 및 검증
- **범위:** Authority 런타임 활성화, 일시 이동 입력, 체크포인트 동기화,
  Zone 접속 상태, Unity 위치 보정

## 의도

화면에 보이는 Unity 플레이어와 무기한 갱신된 별개의 Redis 위치를 근접 행동이
서로 비교하는 문제를 제거합니다.

## 데이터 소유권

- PostgreSQL은 내구 아바타 체크포인트와 작성된 `movement_speed` 최대값을
  소유합니다.
- Redis는 만료되는 세션 입력과 런타임 이동만 소유합니다.
- Unity는 요청 이동을 수집하고 수렴된 Transform을 표현합니다. 최종 거리 판정은
  소유하지 않습니다.

## 결정과 경계

월드 입장은 인증된 런타임 활성화 API를 호출합니다. API는 이전 세션 입력을
지우고 현재 내구 아바타 Transform에서 런타임 이동을 시작합니다. 승인된
체크포인트 명령도 커밋 이후 같은 동기화를 수행합니다.

이동 입력에는 걷기·달리기의 요청 속도가 포함됩니다. Authority는 이를
`movement_speed`로 제한하며, 해당 Fact가 제거되거나 유효하지 않으면 이동도
비활성화됩니다. 활성 Zone 세션이 있는 아바타만 진행되므로 내구 등록 정보가
오프라인 상태에서 Redis 이동을 계속 살려둘 수 없습니다.

프로젝트 소유 월드 씬에서 Unity 수평 위치 보정을 활성화했습니다. 지형, 중력,
수영, 충돌 권위는 아직 별도 어댑터 경계이며 이번 변경이 그것까지 소유한다고
가정하지 않습니다.
입력 어댑터가 `CharacterController.Move`를 단독으로 소유합니다. 수렴기는 제한된
수평 델타만 반환하고, 입력 어댑터가 이동·중력과 같은 한 번의 호출로 합쳐 접지·
충돌 판정이 번갈아 갱신되지 않도록 합니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 걷기·달리기 | 작성 상한 이하의 요청 속도를 그대로 사용 | `RequestedLocomotionSpeedIsClampedByAuthoredMaximum` |
| 제거 | 속도가 없거나 0이면 서버 이동 없음 | `InvalidOrRemovedMovementSpeedDisablesMovement` |
| 새 세션 | 이전 입력 제거 후 시퀀스 1 승인 | `ClearingIntentAllowsANewSessionSequence` |
| Unity 수렴 | 프로젝트 월드에서 Authority 수평 보정 활성화 | `WorldPlayerReconcilesWithActivatedAuthorityMotion` |
| 접지 안정성 | 보정은 Y를 무시하고 단일 이동 호출 전에 제한됨 | `AuthorityCorrectionIsHorizontalAndBounded` |
| 서버 회귀 | Authority 단위 테스트 전체 통과 | 20/20 |
| Unity 회귀 | 전투 EditMode 대상 테스트 통과 | 25/25 |

## 갱신 문서

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
