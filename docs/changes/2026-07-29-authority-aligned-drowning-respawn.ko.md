# Authority와 정렬된 익사 리스폰

## 요약

- **날짜:** 2026-07-29
- **담당:** TOV 개발
- **상태:** 구현 및 검증 완료
- **관련 요청 또는 이슈:** 익사 후 플레이어가 물에 빠진 지점으로 다시 미끄러지는 현상을 막고 리스폰 위치를 고정

## 의도

익사한 플레이어를 하나의 안정된 확정 체크포인트로 복구하고, 과거 Authority
모션이 복구된 Unity 표현을 다시 물속으로 끌어당기지 못하게 합니다.

## 데이터 분류

- [ ] 계정 프로필
- [x] 영속 작성 월드 데이터
- [x] 런타임 관측
- [x] 추론 상태
- [x] Unity 표현
- [x] 전송 / 권한 / 인프라

## 결정과 경계

승인된 아바타 체크포인트가 영속 리스폰 원본입니다. `Drowning`은 추론
상태입니다. Unity는 가라앉기, 순간이동, 수직 속도 초기화, 충돌 지면 정착
표현만 소유합니다. 복구 체크포인트 명령 한 번이 영속 위치를 갱신하고
Authority가 일시 모션을 다시 시작하게 합니다.

물 겹침, 추락, 가라앉는 표현은 안전하지 않은 체크포인트 표본이므로 확정
리스폰 기준을 교체할 수 없습니다. 메시명, 프리팹명, 고정 좌표 예외는 없습니다.
체크포인트 소유자는 로컬 플레이어 입력을 소유한 Actor에서 찾으며, 씬 탐색
순서상 먼저 발견된 임의 Authority 식별자는 유효하지 않습니다.

## 온톨로지 표현

- 관측: `Actor occupies WaterRegion`
- 추론: `Actor movement_mode Drowning`
- 영속 복구 명령: `save_avatar_checkpoint`
- Unity 표현: 가라앉기, 복구, 지면 정착, 위치 보정 중지·재개

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 활성화 / 추가 | `Drowning`이 확정 체크포인트로 복구하고 Authority 모션을 재시드한다. | `DrowningRecoveryUsesConfirmedCheckpointAnchor`, `CheckpointReseedSuspendsStaleAuthorityCorrection` |
| 비활성화 / 제거 | `Drowning`이 없거나 철회되면 복구하지 않고 즉시 이동 제어를 반환한다. | `RetractedDrowningImmediatelyReleasesMovementPresentation` |
| 회귀 / 예외 | 물·추락·활성 복구 위치는 체크포인트를 덮어쓰지 못한다. | `CheckpointCaptureRejectsTransientUnsafePositions` |
| 식별자 회귀 | 계정 입장·원격 제외·체크포인트 소유권은 몬스터가 아니라 로컬 입력 아바타를 선택한다. | `LocalAvatarResolutionIgnoresArbitraryWorldIdentity` |

## 성능과 멀티플레이 영향

복구당 리비전 체크포인트 명령 한 번만 전송하며 프레임별 이벤트를 만들지
않습니다. 위치 보정은 해당 명령이 완료될 때까지만 중지됩니다.

## 갱신한 문서

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] 하네스 시나리오 목록
