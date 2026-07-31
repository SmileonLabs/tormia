# 하네스가 강제하는 무기·몬스터 생산

## 요약

- 날짜: 2026-07-29
- 영역: 개발 정책, 온톨로지 생산 계약, 실행 하네스
- 상태: 구현 및 검증 완료

## 의도

비주얼 자산, 프리팹 동작, 입력 처리기, 애니메이션만 적용한 패치를 완성된
무기나 몬스터로 취급하지 못하게 합니다. 새로 만들거나 마이그레이션·생성·
사용자 작성하는 모든 무기와 자율 몬스터는 canonical 온톨로지 생산 계약을
따라야 합니다.

## 소유권 결정

작성 트리플이 능력과 수치를 소유합니다. 배정 Rule Block이 재사용 행동을
소유하고 불변 액션은 해당 블록을 호출합니다. World Authority가 공유 평가와
영속 결과를 소유합니다. 물리 의미, 부착 프로필, 애니메이션 Manifest,
Unity 어댑터는 게임플레이 권한이나 결과를 만들지 않고 승인 결과만 표현합니다.

## 무기 생산 계약

`weapon-ontology-production`은 프로젝트 소유 리소스 등록, 무기 트리플,
장착·휘두르기·공격 Rule Block, 일치하는 불변 액션, 물리 의미와 부착·그립
데이터, 검증된 애니메이션·VFX 콘텐츠, Authority 평가, Unity 표현, 실행
가능한 블록 제거 증거를 요구합니다.

## 몬스터 생산 계약

`monster-ontology-production`은 프로젝트 소유 비주얼·카탈로그 등록, 자율
액터 트리플, 배정 자율 Rule Block, 일치하는 불변 공격 액션,
`AuthorityKinematic` 물리 의미, 검증된 애니메이션·Profile 콘텐츠,
Authority 시뮬레이션과 영속 전투 결과, Unity 보간·표현, 실행 가능한 계약
제거 증거를 요구합니다.

## 하네스 강제

`scripts/verify-development.ps1`는 두 생산 계약 중 하나가 없거나 중복·이름
변경되거나 잘못된 시나리오에 연결된 경우, 필수 9단계 중 하나가 누락된 경우,
활성·제거 기대 결과가 없는 경우, 금지 구현 선언이 부족한 경우, 기록된 테스트
파일에 실행 증거가 없는 경우 실패합니다.

금지 구현에는 이름 기반 게임플레이 분기, Unity가 소유하는 영속 결과,
Rule Block 결과를 중복하는 직접 액션 효과, 계약 제거 뒤에도 남는 fallback,
검증 Manifest를 우회하는 애니메이션·VFX 등록이 포함됩니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 완전한 무기 계약 | 필수 9단계·금지 항목·실행 증거가 모두 존재 | `rule-block-owned-primary-melee-attack` |
| 완전한 몬스터 계약 | 필수 9단계·금지 항목·실행 증거가 모두 존재 | `portable-autonomous-monster-ontology-contract` |
| 단계 누락 또는 우회 | 재사용 생산 콘텐츠로 인정되기 전에 개발 검증 실패 | `scripts/verify-development.ps1` |

최종 검증은 `-RequireServices -RequireUnityMcp` 옵션으로 통과했습니다.
한·영 컨텍스트, 핵심 회귀·생산 계약, 영속 Unity MCP, World Authority
Docker 빌드, PostgreSQL·Redis·Authority 서비스와 Authority 상태를 모두
확인했습니다.

## 갱신 문서

- `AGENTS.md`
- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
- `scripts/verify-development.ps1`
