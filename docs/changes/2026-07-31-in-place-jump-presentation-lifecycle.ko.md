# 인플레이스 점프 표현 생명 주기

## 요약

- **날짜:** 2026-07-31
- **담당:** TOV 게임플레이/온톨로지
- **상태:** 구현 및 검증 완료
- **요청:** 로컬 점프 덧대기를 추가하지 않고 착지 뒤 두 번째로 튀어 보이는 현상을 제거합니다.

## 의도

승인된 포물선 이동은 단일 CharacterController가 계속 소유하고, 승인된
점프 한 번이 추적 가능한 표현 생명 주기를 한 번만 만들도록 합니다.

## 데이터 분류

- 런타임 관측: 접지 상태, 수직 속도, 점프 발생 번호
- 추론된 표현 상태: `JumpStart`, `Airborne`, `Fall`, `Landing`
- Unity 표현 데이터: Manifest 재생 구간과 Root Motion 모드
- 영속 게임플레이 권한은 기존 `jump_action`과
  `JumpPlayerFromIntent`에 그대로 유지합니다.

## 결정과 경계

승인된 점프는 일시적인 발생 번호를 한 번 증가시킵니다. 애니메이션
해석기는 이 발생을 `JumpStart`로 소비하고, 수직 충돌 상태로
`Airborne`과 `Fall`을 선택하며, 지지면이 돌아오면 `Landing`에 한 번
진입합니다. 일회성 단계는 보통 고정 타이머가 아니라 선택된 클립 구간의
완료로 끝납니다. 다만 충돌 해결 결과로 관측한 정점은 더 이른 물리
경계이므로, 상승보다 긴 이륙 클립은 하강 중 계속 선택될 수 없습니다.

CharacterController만 월드 이동을 소유합니다. 점프 생명 주기 항목은
Root Motion 비활성화를 명시해야 합니다. 서드파티 원본 클립은 수정하지
않고 프로젝트 Manifest가 짧은 `JumpStart` 구간과 `Jump_End`의 단조롭게
안정화되는 0.85~1.00 구간만 선택합니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 활성 | 승인 한 번이 발생 한 번과 순서가 보장된 표현 생명 주기 한 번을 만듭니다. | `ApprovedJumpOccurrenceCompletesEachPresentationPhaseOnce`; `ApprovedJumpCreatesOneOccurrenceAndConsumesSupport` |
| 정점 | 음의 수직 속도가 지나치게 긴 `JumpStart`를 끝내고 `Fall`을 선택합니다. | `DescendingAvatarCannotRemainInJumpStart` |
| 콘텐츠 | 점프 클립은 Root Motion 비활성화를 명시하고 착지 구간에는 상승 Root 곡선이 없습니다. | `PlayerJumpLifecycleUsesInPlaceSegmentsWithoutLandingRise` |
| 잘못된 설정/제거 | 잘못된 구간이나 상속된 점프 Root Motion은 검증 실패하며, 지지면 소비 뒤 두 번째 승인은 거절됩니다. | `JumpLifecycleManifestRequiresSegmentedInPlacePresentation`; PlayMode 발생 테스트 |

## 성능과 멀티플레이 영향

발생 번호는 로컬 일시 상태입니다. 프레임별 Fact, Authority 명령, DB
이벤트 또는 추가 Transform solver를 만들지 않습니다.

## 갱신 문서

- `AGENTS.md`
- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
