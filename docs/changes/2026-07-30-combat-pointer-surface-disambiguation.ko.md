# 전투 포인터의 표면 클릭 구분

## 요약

- **날짜:** 2026-07-30
- **상태:** 구현 완료
- **문제:** 몬스터의 화면상 아래쪽을 클릭하면 Trigger를 근소하게 빗나가
  같은 월드 영역의 지면을 맞출 수 있었습니다. 공유 클릭이 이동으로
  전달되어, 이후 클릭이 우연히 Trigger를 맞출 때까지 공격이 늦게
  시작되는 것처럼 보였습니다.

## 결정

- 첫 Projection 비전투 엔티티에서 멈추지 않고, Projection 전투 대상
  Presenter를 찾을 때까지 정확한 레이 충돌을 계속 확인합니다.
- 정확한 전투 충돌이 없으면, 활성 Projection 전투 대상 바로 아래의 지면
  클릭을 작성된 상호작용 Collider 영역, 작은 직렬화 여유값, 열린 카메라
  시야를 사용해 같은 대상으로 복원할 수 있습니다.
- 이 처리는 휘발성 Unity 관찰로만 유지합니다. 적대 관계, 생존, 사거리,
  쿨다운, Rule Block 배정, 애니메이션 승인, 접촉, 피해는 계속 World
  Authority가 소유합니다.

## 활성 및 제거 경로

- 작성된 공격·휘두르기 Rule Block이 있으면 대상 또는 화면상 발밑 영역
  클릭이 기존 Authority 사전 평가 경로로 들어갈 수 있습니다.
- 어느 Rule Block이나 필수 Fact 또는 Projection 전투 Presenter를 제거해도
  Unity 공격 fallback은 생기지 않으며 클릭은 이동 입력으로 남습니다.

## 증거

- `CombatPointerAssist_UsesOnlyAuthoredColliderFootprint`
- `CombatPointerAssist_RecoversGroundHitBeneathProjectedTarget`
- 기존 `RemovingPrimaryAttackRuleBlockRemovesAttackBehavior`
- 기존 `PrimaryAttackRejectsFriendlyOrDefeatedTarget`

## 검증

- Unity EditMode: 240/240 통과
- Unity PlayMode: 53/53 통과
- `scripts/verify-development.ps1 -RequireServices -RequireUnityMcp`: 통과
