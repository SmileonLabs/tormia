# 회전된 실제 표면 기준 근접 접근

## 요약

- **날짜:** 2026-07-30
- **상태:** 구현
- **문제:** 이동 정지 계산이 수직 무기 Box의 대각선을 수평 사거리로
  포함하여, Authority가 승인한 휘두르기가 간헐적으로 접촉 없이 끝났습니다.
  접촉을 바로잡은 뒤에도 이동 여유값이 플레이어·대상 몸체 표면의 합보다
  정지 반경을 작게 만들어 두 캐릭터가 시각적으로 겹칠 수 있었습니다.

## 결정

- 현재 회전된 작성 무기 접촉 표면과 승인 대상 콜라이더의 가장 가까운
  점으로 클릭 접근 거리를 계산합니다.
- Manifest 접촉 구간 전의 매 프레임에는 피해를 보고하지 않고 이전 접촉
  Pose만 샘플링합니다.
- 플레이어 컨트롤러와 대상 상호작용 콜라이더를 작성된 Physical Meaning
  표면으로 사용합니다. 두 표면의 수평 간격은 공격 사거리 이동 여유값이
  줄일 수 없는 최소 정지 거리입니다.
- 접촉 관찰은 비영속으로 유지하며 피해·쿨다운·체력·사망·전리품은 계속
  World Authority만 소유합니다.

## 활성·제거 경로

- 접촉 모드 트리플, 배정된 공격 Rule Block, 검증된 Manifest 구간, 승인
  대상과의 실제 겹침이 모두 있으면 기존 Authority 피해 액션을 요청합니다.
- 필수 의미 단계가 제거되거나 실제로 겹치지 않으면 피해가 없으며 시간,
  레이캐스트, 이름, 거리 fallback도 만들지 않습니다.

## 증거

- `ContactApproachUsesOrientedWeaponSurface`
- `PrimedContactSampleSeedsFirstApprovedSweep`
- `CombatBodyClearanceUsesAuthoredColliderSurfaces`
- `CombatApproach_NeverConsumesPhysicalBodyClearanceAsRangeInset`
- 기존 접촉 전달 및 Rule Block 제거 회귀 테스트

## 검증

- Unity EditMode: 238/238 통과
- Unity PlayMode: 53/53 통과
- `scripts/verify-development.ps1 -RequireServices -RequireUnityMcp`: 통과
