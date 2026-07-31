# 이동 승인과 접지 연속성

## 요약

- **날짜:** 2026-07-31
- **담당:** TOV 팀
- **상태:** 검증 완료
- **관련 요청:** 가까운 몬스터가 플레이어를 들어 올리는 것처럼 보이는 현상

## 의도

이동과 무관한 전투 Projection 변경이 플레이어 접지를 중단하거나 공중·점프
표현을 잘못 만들지 않게 합니다.

## 데이터 분류

- [ ] 계정 프로필
- [ ] 지속 작성 월드 데이터
- [x] 런타임 관측
- [x] 추론 상태
- [x] Unity 표현
- [x] 전송 / Authority / 인프라

## 결정과 경계

새 이동·점프 의도의 승인 여부는 계속 World Authority가 소유합니다.
Unity는 이미 보이는 캡슐에 작성된 중력을 연속 표현하고, 무효화된 이동
lease를 0 이동량 일시 샘플로 자동 재검증하며, 관측된 지면 접촉으로 공중
표현을 선택할 수 있습니다. 이 과정은 게임플레이 권한이나 영속 Fact를
만들지 않습니다.

## 온톨로지 표현

- 이동 지문에는 이동·점프 계약 입력만 포함합니다.
- 전투, 장착, 프로필, 리스폰, 표현 의미는 제외합니다.
- 관련 계약을 제거하면 완전한 계약을 Authority가 다시 승인할 때까지
  예측 이동은 계속 중단됩니다.
- 중력은 Physical Meaning이며 점프 시작은 Rule과 Authority가 소유합니다.
- 자동 lease 복구는 명시적 0 이동량 월드 입장 준비가 성공한 뒤에만
  활성화하여 두 일시 요청의 sequence가 경쟁하지 않게 합니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 활성 / 추가 | 최초 입장 준비는 단독 실행되며, 이후 전투·장착 revision이 이동 승인을 유지하고 완전한 계약은 자동 재검증되며 작성된 중력이 캡슐을 접지시킵니다. | `RuntimeRevalidationWaitsForInitialEntryPreparation`, `UnrelatedAvatarCombatStatePreservesApprovedLocomotionContract`, `AuthoredGravityContinuesSettlingWithoutNewMovementIntent` |
| 비활성 / 제거 | 관련 이동 Fact, Rule Block, 월드·Zone 범위, 이동 액션을 변경·제거하면 Authority 재승인 전까지 예측 이동이 멈춥니다. | `ChangedOrRemovedPlayerLocomotionContractRequiresReapproval` |
| 표현 예외 | 승인된 점프 없이 접촉만 끊기면 Airborne/Fall을 표현하며 JumpStart는 표현하지 않습니다. | `LosingGroundWithoutApprovedJumpDoesNotPresentJumpStart`, `scripts/verify-development.ps1` 통과 |

## 성능과 멀티플레이 영향

재검증은 일시적인 0 이동량 샘플을 사용하고 거절 후 재시도 속도를 제한합니다.
월드 revision을 증가시키거나 프레임별 Fact를 기록하지 않습니다.

## 갱신 문서

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] 테스트 시나리오 목록
