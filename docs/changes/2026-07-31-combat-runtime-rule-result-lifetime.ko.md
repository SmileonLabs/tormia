# 전투 런타임과 규칙 결과 수명

## 요약

- **날짜:** 2026-07-31
- **담당:** TOV 팀
- **상태:** 검증 완료
- **관련 요청:** 월드 입장·온톨로지 리팩터링 후 몬스터가 공격하지 않는 문제

## 의도

플레이어가 Zone에 입장한 동안 자율 전투를 안정적으로 유지하고, 액션 해석의
모호성을 없애며, Rule Block 이관·제거가 이미 승인된 전투 상태를 되감지 않게
합니다.

## 데이터 분류

- [ ] 계정 프로필
- [x] 지속 작성 월드 데이터
- [x] 런타임 관측
- [x] 추론 상태
- [x] Unity 표현
- [x] 전송 / Authority / 인프라

## 결정과 경계

타깃, 추격, 공격, 체력, 사망, 리스폰, 드롭 평가는 World Authority만
소유합니다. Unity는 `InWorld` 이후 인증된 런타임 존재 lease 갱신,
입력·관측 전송, 표현만 담당합니다. SignalR은 게임플레이 가용성의 필수
조건이 아닙니다. Unity는 Rule Block을 복제하거나 이름에서 액션 정체성을
추론하지 않습니다.

## 온톨로지 표현

- 액션 참조 관계는 `ActionRef` 종류를 사용하여 정확한 불변 ID를 유지합니다.
- 단일 카디널리티 의미 패키지 관계는 이전 값을 원자적으로 교체합니다.
- 규칙 효과는 `RuleBound` 또는 `DurableState`를 선언합니다.
- 기본 `RuleBound`는 과거에 생략된 wire 값과 같게 정규화하여 스키마
  확장 때문에 거짓 불변 버전 충돌이 발생하지 않게 합니다.
- 명시적인 `DurableState`는 계속 불변 Rule 내용으로 유지합니다.
- `MeleeAttackOnPrimaryIntent`, `AutonomousMeleeCombat`,
  `RespawnPlayerOnDeath`, `CollectAvailableLoot`는 승인된 상태 전이를
  지속합니다.
- `EquipItemOnInteractionIntent`는 규칙 종속 상태를 유지합니다.
- 플레이어 의미 계약 버전 4는 기존 의미 데이터에서 체력을 복구하고
  활성화된 리스폰 바인딩만 게시 버전으로 이관합니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 활성 / 추가 | HTTP Zone lease가 자율 스케줄링을 유지하고 정확한 액션 ID가 해석되며 승인된 공격·리스폰 결과가 지속됩니다. | `scripts/verify-development.ps1 -RequireServices -RequireUnityMcp`, World Authority 테스트 61개 통과, Unity 핵심 EditMode 테스트 14개 통과, 실제 입장 상태 `InWorld` 확인 |
| 비활성 / 제거 | 필수 Rule Block을 제거하면 이후 동작이 멈추고 규칙 종속 장착은 철회되지만 승인된 체력·사망 이력은 남습니다. | `AuthoritativeActionEvaluatorTests`, `OntologyCombatVerticalSliceAssetTests` |
| 회귀 / 예외 | 기존 액션 Fact는 불변 규칙 내용으로만 수명이 승격되고 제거된 플레이어 바인딩은 재생성되지 않습니다. | `ContentRuleCanonicalizationTests`, 플레이어 의미 이관 검사 |
| 스키마 호환 | 새로 직렬화된 기본 `RuleBound`는 과거 생략 필드와 wire 의미가 같고, 명시적 `DurableState`는 불변 내용으로 남습니다. | `DefaultRuleBoundLifetimePreservesLegacyImmutablePayload`, `DurableLifetimeRemainsImmutableRuleContent` |

## 성능과 멀티플레이 영향

활성 Unity Zone 세션마다 설정된 하트비트 간격으로 인증된 HTTP lease 하나를
갱신합니다. 월드 revision을 증가시키거나 프레임별 Fact를 기록하지 않습니다.
기존 Redis Zone 세션 만료와 Authority 스케줄러가 계속 권한을 소유하므로
멀티플레이 경계가 유지됩니다.

## 갱신 문서

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] SQL 마이그레이션
- [x] 테스트 시나리오 목록
