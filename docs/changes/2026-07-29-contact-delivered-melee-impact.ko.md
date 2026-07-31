# 실제 접촉 기반 근접 타격

## 요약

- **날짜:** 2026-07-29
- **담당:** TOV 개발
- **상태:** 구현 및 Unity 자동 검증 완료, 수동 전투 확인 예정
- **관련 요청:** 검이 몬스터에 닿을 때 피해와 타격 표현이 발생하도록 개선

## 의도

대상 레이캐스트 시점에 실행하던 타격을 작성된 애니메이션 접촉 구간과
Authority 사전 승인 대상에 대한 실제 무기 볼륨 겹침으로 교체합니다.

## 데이터 분류

- 영속 작성 월드 데이터: `attack_contact_mode`
- 런타임 관측: 무기와 승인 대상의 겹침
- Unity 표현: 애니메이션 시간, 접촉점, VFX
- 권한 전송: 사전 평가 뒤 revision 영속 액션 실행

## 결정과 경계

피해·사망·전리품·사거리·쿨다운은 World Authority만 소유합니다. Unity는
애니메이션 재생과 비영속 접촉 관찰만 소유하며, 경과 시간·레이캐스트
지점·에셋 이름 기반 피해 fallback은 두지 않습니다.

## 온톨로지 표현

- 트리플: `tool attack_contact_mode WeaponContactWindow`
- Rule Block: `MeleeAttackOnPrimaryIntent`
- 물리·표현 데이터: 무기 프리팹에 작성된 `BoxCollider`
- 애니메이션 데이터: `Anim_Sword_LightAttack` 정규화 접촉 구간
- 이동 표현: Authority 작성 공격 사거리 이내에서 무기·대상 콜라이더
  형상으로 접근 정지 거리를 계산

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 활성화 | Manifest 구간 안에서 승인 대상과 겹치면 Authority 피해 요청 | `MeleeDamageRequiresAuthoredContactModeAndAnimationWindow`, `DisabledWeaponColliderStillObservesOnlyApprovedContact` |
| 비활성화 / 제거 | Rule Block·접촉 트리플·구간·겹침 중 하나라도 없으면 피해/VFX 없음 | 에셋 계약 테스트와 기존 Rule Block 제거 Authority 테스트 |
| 회귀 | Manifest 접촉 구간이 런타임 Database에 동기화 | `CurrentAnimationAssetsMatchManifestProjection` |

2026-07-29 기준 관련 EditMode 세 묶음 68개 테스트가 모두 통과했습니다.

## 성능과 멀티플레이 영향

겹침 질의는 비반복 공격의 접촉 구간에서만 실행하며 프레임별 DB 이벤트를
만들지 않습니다. 접촉 뒤 Authority가 영속 액션을 다시 평가하므로
멀티플레이 상태의 소유권도 서버에 유지됩니다.

## 갱신한 문서

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
