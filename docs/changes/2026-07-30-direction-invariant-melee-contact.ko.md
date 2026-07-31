# 방향에 무관한 근접 접촉

## 요약

- **날짜:** 2026-07-30
- **상태:** 구현 및 Unity 자동·실제 런타임 검증 완료
- **문제:** 일부 월드 방향에서 대상 클릭 공격이 빗나감

## 런타임 증거

Authority 사전 평가와 휘두르기 승인은 성공했지만 실패 경로는
`attack_contact_window_completed_without_contact`에서 끝났습니다. 실시간
점검 결과 대상 방향과 11.4도 어긋나 있었고, 접촉 Adapter는 현재 프레임의
무기 Box Pose만 검사하고 있었습니다.

## 결정

- 대상 공격 의도를 발행하기 전에 선택된 Projection 대상을 바라봅니다.
- 이전·현재 작성 무기 접촉 볼륨 사이의 이동과 회전을 연속 샘플링합니다.
- 모든 샘플은 비영속이며 Authority 승인 대상과 Manifest 접촉 구간으로
  제한합니다.

방향·무기명·프리팹명·애니메이션명·몬스터명 예외는 없습니다. 영속 피해와
전투 결과는 계속 World Authority가 소유합니다.

## 검증

- `CombatFacing_RequiresTargetDirectionBeforeAttackPublication`
- `WeaponContactSweepSubstepsCoverLinearAndAngularMotion`
- `WeaponContactSweepObservesTargetSkippedBetweenFrames`
- 관련 Unity EditMode 테스트: 72/72 통과
- 실제 경로: 기존 실패 방향에서 `approved_weapon_contact_observed` 이후
  `damage_authority_accepted` 확인
