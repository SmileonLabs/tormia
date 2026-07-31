# 공중 단차 보정 일시 중단

> `2026-07-31-single-owner-collision-resolved-player-motion.ko.md` 결정으로
> 대체되었습니다. 내장 단차 값은 이제 항상 0이며 명시적 단차 판정은 작성된
> `WalkableSupport` 충돌 역할만 허용합니다.

## 결정

`CharacterController.stepOffset`은 지지가 확인된 지상 이동에서만
사용합니다. Unity 이동 어댑터는 이륙, 낙하 및 지지가 없는 하강 중
단차 보정을 중단하고 지지가 다시 확인되면 씬에 작성된 값을 복원합니다.

## 이유

점프 애니메이션 발생은 이미 수직 이동과 분리됐지만, 하강 중인 캡슐이
바닥 이음새나 오브젝트 모서리를 오를 수 있는 계단으로 해석할 수
있었습니다. 이 경우 작성된 수직 속도가 양수가 아닌데도
CharacterController가 Transform을 위로 이동시킬 수 있습니다.

## 계약

- Authority와 Rule Block이 점프 권한과 수치를 계속 소유합니다.
- Unity는 일시적인 충돌 관찰과 단차 표현만 소유합니다.
- 착지는 하강 속도를 제거할 수 있지만 위쪽 속도를 만들 수 없습니다.
- 점프를 제거해도 지지가 확인된 일반 계단 이동은 유지됩니다.

## 증거

- `FallingFromRaisedSupportCannotUseStepClimbToMoveUp`
- `ApprovedParabolicJumpUsesOneControllerMoveAndLands`
