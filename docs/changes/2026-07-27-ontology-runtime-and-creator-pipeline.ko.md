# 런타임 Fact 소유권과 제작 파이프라인

## 요약

- **날짜:** 2026-07-27
- **담당:** TOV 개발
- **상태:** 구현 및 대상 검증 완료, 전체 Unity 테스트는 에디터 프로젝트 잠금
  해제 후 실행 필요
- **요청:** 전문 NPC 기능을 만들기 전에 하드코딩과 온톨로지 소유권 문제 정리

## 의도

Unity 이름, 관측값, 임시 제작 UI가 영속 게임 진실이 되지 않도록 정리하고,
첫 프롬프트 기반 UGC 수직 슬라이스를 안전하게 시작합니다.

## 데이터 분류

- [ ] 계정 프로필
- [x] 영속 작성 월드 데이터
- [x] 런타임 관측
- [x] 추론 상태
- [x] Unity 표현
- [x] 전송 / 권한 / 인프라

## 결정과 경계

- 씬의 중복 `entityId: Player` 샘플을 삭제했습니다. 실제 플레이어는 기존
  프로필 기반 구조를 유지합니다. 샘플의 FireSword와 스킬 Fact는 검증된
  계정 프로필이나 작성 월드 데이터가 아니므로 이전하지 않았습니다.
- 현재 타일, 근접, 클릭 의도, 시뮬레이션 틱은 `RuntimeObservation` 기여를
  사용하고 해당 출처만 회수합니다.
- 런타임 타일 이동은 더 이상 저장 가능한 세션 이벤트를 만들지 않습니다.
- `OntologyObject.EntityId`는 GameObject 이름으로 대체되지 않습니다. 새 배치
  엔티티는 Authority 식별 GUID를 사용하고, 저장 버전 8은 표시 이름과 ID를
  분리합니다.
- 제작자 역할과 임시 외형은 `CreatorServiceCatalog.asset`에서 읽습니다.
- NPC 액션 우선순위와 로컬 자동 실행은 명시적인
  `OntologyNpcDecisionPolicy`가 필요하며, 컨트롤러에 숨은 help/talk 기본값을
  두지 않습니다.
- 월드 설계자 프롬프트는 메모리의 Draft와 제작 경로만 만듭니다. 월드 Fact와
  Authority 명령은 만들지 않습니다.

## 온톨로지 표현

새 게임플레이 관계는 추가하지 않았습니다. Fact 출처와 안정적인 Subject를
보호합니다. 전문 서비스 canonical ID는 `WorldArchitect`, `ResourceMaker`,
`OntologySteward`, `PhysicsEngineer`, `RuleEngineer`, `QuestDesigner`,
`UiDesigner`, `QaPublisher`입니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 런타임 관측 제거 | `RuntimeObservation` 기여만 제거 | `OntologyWorldStateTests` |
| 같은 영속 Fact가 나중에 추가됨 | 관측 회수 뒤에도 유지 | `OntologyWorldStateTests` |
| 월드 재구성 | 소유하던 런타임 단일 값을 재주입 | `OntologyWorldStateTests` |
| GameObject 이름 변경 | 안정 ID 불변 | `OntologyStableEntityIdentityTests` |
| 유효한 설계 프롬프트 | 순서화된 Draft, 월드 Fact 0개 | `OntologyCreatorWorkDraftTests` |
| 짧은 프롬프트 / 설계자 없음 | Draft 거부 | `OntologyCreatorWorkDraftTests` |
| 중복 Player | `TormiaWorld`에 명시 `entityId: Player` 하나 | 씬 감사 |

2026-07-27에 완료한 검증:

- `scripts/verify-development.ps1` 통과
- `scripts/verify-development.ps1 -RequireServices` 통과:
  PostgreSQL, Redis, World Authority 정상
- Core, Unity, UI, Editor, Core.Tests, Unity.Tests 어셈블리 오류 없이 컴파일
- 런타임 관측 및 제작 Draft 대상 테스트 5개 통과
- 전체 EditMode 러너는 실행 중인 Unity 에디터가 프로젝트 잠금을 보유해
  프로젝트를 다시 열 수 없었습니다. 테스트 실패가 아닌 실행 환경 제약이며,
  에디터 종료 또는 재연결 후 전체 스위트를 실행합니다.

## 성능과 멀티플레이 영향

이동 관측 이벤트를 제거해 영속 기록 부담을 줄였습니다. 제작 Draft는 로컬
임시 상태입니다. 새 DB 또는 네트워크 쓰기 경로는 추가하지 않았습니다. 이후
영속 제작 결과는 리비전과 멱등성을 가진 World Authority 명령을 사용해야
합니다.

## 갱신 문서

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] 영문/한글 언어팩 CSV
- [x] 테스트와 변경 기록
