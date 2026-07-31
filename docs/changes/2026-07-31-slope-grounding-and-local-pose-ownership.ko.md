# 경사 접지와 로컬 위치 소유권

> 이 기록의 지지면 접선 이동 부분은
> `2026-07-31-single-pass-planar-character-collision.ko.md`로 대체합니다.
> 지지면 법선은 관측값으로만 유지하며 더 이상 수직 이동을 만들지 않습니다.

## 요약

- **날짜:** 2026-07-31
- **대상:** 플레이어 충돌 표현과 Authority 투영 소유권
- **상태:** 구현 및 검증 완료
- **관련 문제:** 착지 전에 플레이어가 튀거나 접촉 중 미끄러지고, 이전 저장
  위치 쪽으로 돌아가는 현상

## 의도

Unity에 새로운 게임플레이 권한을 주지 않으면서 로컬 충돌 해결을 안정화합니다.
이동 허용 여부는 기존의 완전한 작성형 온톨로지 계약이 계속 결정합니다. 이번
변경은 충돌 관찰과 영속 투영 갱신이 서로 경쟁하는 Transform 작성자가 되지
않도록 합니다.

## 데이터 분류

- [ ] 계정 프로필
- [ ] 영속 작성형 월드 데이터
- [x] 임시 관찰
- [x] 추론/런타임 상태
- [x] Unity 표현
- [x] 전송 / Authority / 인프라 경계

## 결정과 경계

지면 probe의 근접 감지는 단차 상승을 허용하지 않습니다. 명시적인 단차 상승은
CharacterController가 실제로 접지된 경우에만 가능하고, 걸을 수 있는 경사면
법선은 연속 지형으로 처리합니다. 수평 이동은 지지면 접선을 따르지만 중력과
접지는 세계 수직 방향을 유지합니다. CharacterController를 직접 측정한 결과,
지지면 법선 방향 접지는 평면 성분을 보존해 역방향 경사 미끄러짐을 만들었습니다.

월드 세션 중 로컬 아바타는 의미 재연결의 짧은 순간과 Rule Block 제거 경로에서도
화면상 런타임 위치 소유권을 유지합니다. 물리 의미를 제거하면 이동 코디네이터는
계속 비활성화됩니다. 다만 오래된 영속 투영이 아바타를 이전 위치로 되돌리지는
못하며, 입장·복구·리스폰만 명시적인 영속 위치 복원 경계로 남습니다.

## 온톨로지 표현

새 Triple, Rule Block, 물리 의미는 추가하지 않았습니다.
`LocalCharacterController` 물리 의미가 계속 이동을 활성화하고,
`WalkableSupport`가 지형의 충돌 역할을 담당합니다. 지지면 법선·접지 접촉·충돌
플래그는 Unity의 임시 관찰입니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 활성 | 경사 접지가 평면 미끄러짐을 만들지 않고 연속 경사면이 단차 상승을 만들지 않습니다. | `GroundAdhesionOnWalkableSlopeDoesNotCreatePlanarDrift`, `WalkableSlopeIsNotMisclassifiedAsExplicitStep` |
| 제거 | 물리 의미 제거가 이동만 비활성화하고 로컬 표시 위치를 영속 투영에 넘기지 않습니다. | `LocalCharacterPhysicalMeaningOwnsAndRemovesMotionLease` |
| 경계 | 착지 전 지면 근접 감지가 위쪽 단차 변위를 만들지 않고 ActorBody는 장애물로 남습니다. | `SupportProximityBeforeLandingCannotCreateStepRise`, `ActorBodyCannotBeUsedAsACharacterStep` |

Unity 검증에서 하이브리드 이동 PlayMode 7/7, 의미 동기화 PlayMode 14/14,
플레이어 입력·소유권 EditMode 26/26이 통과했습니다. 검증 후 Unity Console에는
오류가 없습니다.

## 성능과 멀티플레이 영향

이미 bounded non-alloc probe가 만든 지지면 법선을 재사용합니다. 추가 polling,
영속 이벤트, 네트워크 메시지, DB 쓰기는 없습니다. Authority는 계속 이동 권한을
검증하고 Unity는 충돌 해결된 위치를 임시 샘플로만 보냅니다.

## 갱신 문서

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] 핵심 하네스 시나리오 목록
