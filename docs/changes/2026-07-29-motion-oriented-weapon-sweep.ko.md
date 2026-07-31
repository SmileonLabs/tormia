# 실제 이동 방향을 따르는 무기 궤적

## 요약

- **날짜:** 2026-07-29
- **담당:** TOV 개발
- **상태:** 구현 및 Unity 자동 검증 완료
- **관련 요청:** 검 휘두르기 궤적을 애니메이션의 실제 무기 이동 방향에 맞춤

## 결정

기존 카탈로그가 선택하는 Slash VFX는 표현 자산으로 유지하되, 작성된 검
길이축과 편집 가능한 Slash Anchor의 프레임별 실제 이동으로 Anchor 방향을
계산합니다. 이펙트는 Animation Manifest 접촉 구간 안에서만 시작합니다.

## 온톨로지 경계

- 트리플과 Rule Block: 기존 Authority 승인 휘두르기 계약
- Animation Manifest: 허용된 표현·접촉 구간 소유
- Unity Adapter: 검 이동을 관찰하고 동적 VFX Anchor 방향 계산
- World Authority: 피해·쿨다운·사망·전리품을 계속 소유

휘두르기 Rule Block 또는 승인된 애니메이션 의도를 제거하면 궤적도
사라집니다. 시간·무기명·프리팹명·애니메이션명 fallback은 없습니다.

## 검증

- `WeaponSweepOrientationFollowsAuthoredBladeAndMotionAxes`
- 관련 Unity EditMode 테스트: 69/69 통과
- `scripts/verify-development.ps1 -RequireUnityMcp`
