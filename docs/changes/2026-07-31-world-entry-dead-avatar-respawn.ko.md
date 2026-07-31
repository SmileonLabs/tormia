# 사망 아바타의 월드 입장 리스폰

## 요약

- **날짜:** 2026-07-31
- **담당:** TOV 팀
- **상태:** 구현
- **관련 요청:** 저장된 사망 아바타가 월드에 입장하지 못하는 문제

## 의도

사망 아바타는 월드 입장을 위해 이동 승인이 필요하지만, 자동 리스폰은 이미
월드에 입장한 뒤에만 실행되던 순환 의존성을 제거합니다.

## 데이터 분류

- [ ] 계정 프로필
- [x] 지속 작성 월드 데이터
- [x] 런타임 관찰
- [x] 추론 상태
- [x] Unity 표현
- [x] 전송 / Authority / 인프라

## 결정과 경계

월드 Projection을 불러온 직후, 이동 승인을 요청하기 전에 아바타의 작성된
생명 상태를 확인합니다. 사망 상태라면 Unity는 canonical
`respawn_action`을 제출하고, 배정된 `RespawnPlayerOnDeath` 규칙 블록이
World Authority를 통해 체력과 생명을 복구합니다. 다시 불러온
Projection에서 `is_alive=true`가 검증된 뒤에만 입장을 계속합니다.
Unity는 아바타를 로컬에서 살리거나 체력을 임의로 만들지 않습니다.

같은 입장 과정에서 실행되는 이전 의미 계약 복구 명령은 클라이언트의 revision
재시도 전송을 사용합니다. 자율 월드 revision이 동시에 갱신되어도 정상적인
의미 패키지 이관이 한 번의 `stale_revision` 거절로 입장을 중단하지 않습니다.

사망 표현이 플레이어 `CharacterController`를 비활성화하면 연속 중력 표현도
그 컴포넌트 경계에서 멈춥니다. 지속 사망 상태는 Authority가 계속 소유하면서
Unity가 비활성 Controller에 잘못된 `Move`를 호출하지 않게 합니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 활성 / 추가 | 사망 아바타가 이동 준비 전에 활성화된 Authority 리스폰 계약으로 복구됩니다. | `DeadAvatarRequiresAuthorityRespawnBeforeWorldEntry`; `WorldEntryRestoresLifeBeforeLocomotionHandshake` |
| 비활성 / 제거 | 리스폰 의미가 누락·충돌·비활성 상태면 숨은 Unity 대체 로직 없이 월드 입장이 실패합니다. | `ConflictingAliveProjectionDoesNotTriggerRespawn`; `RemovingPlayerRespawnRuleBlockRemovesRespawnBehavior` |
| 동시 revision | 입장 의미 계약 복구가 Authority의 stale revision 응답을 새 명령 봉투로 한 번 재시도합니다. | `EntrySemanticRepairUsesRevisionRetryTransport` |

## 갱신 문서

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] 테스트 시나리오 목록
