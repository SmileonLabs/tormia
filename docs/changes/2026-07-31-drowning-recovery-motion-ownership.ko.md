# 익사 복구 이동 소유권

## 요약

- **날짜:** 2026-07-31
- **상태:** 구현 및 검증 완료
- **요청:** 플레이어가 물에 들어갈 때 자동 점프처럼 보이고 이동이 멈추는 현상 제거

## 의도

익사 표현이 `CharacterController`를 일시적으로 끈 뒤에도 온톨로지에서
파생된 익사 복구가 끝까지 진행되도록 합니다.

## 데이터 분류

- 런타임 관찰
- 추론 상태
- Unity 표현

## 결정과 경계

`movement_mode Drowning`은 계속 온톨로지가 추론한 결과입니다. Unity는 그
결과를 표현하지만 메시나 오브젝트 이름으로 익사를 추론하지 않습니다.
익사 복구는 일반 플레이어 입력과 컨트롤러 이동보다 먼저 평가합니다.
복구가 이동을 소유하는 동안 임시 속도와 입력 표현을 초기화하며, 관계가
제거되면 즉시 표현 소유권을 해제합니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 활성 | 컨트롤러를 끈 뒤에도 복구가 진행되어 확정 기준 위치로 돌아갑니다. | `OntologyInferenceSchedulingTests.PlayerInputAdvancesRecoveryAfterControllerIsDisabled` |
| 비활성 | `Drowning`을 제거하면 컨트롤러를 복원하고 복구 소유권을 해제합니다. | `OntologyInferenceSchedulingTests.RetractedDrowningImmediatelyReleasesMovementPresentation` |
| 회귀 | 기존 체크포인트 복구와 하이브리드 충돌 이동이 계속 유효합니다. | `OntologyInferenceSchedulingTests`; `OntologyHybridCharacterMotionTests` |

## 성능과 멀티플레이 영향

프레임마다 영속 이벤트를 추가하지 않습니다. 복구는 Authority 파생 상태의
로컬 표현이며 기존 확정 복구 체크포인트만 영속 저장합니다.

## 갱신 문서

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
