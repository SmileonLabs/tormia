# 크리에이터 작업공간 전문 NPC

## 요약

- **날짜:** 2026-07-24
- **담당:** Tormia 팀
- **상태:** 첫 표현 슬라이스 검증 완료
- **관련 요청:** 플레이어 캐릭터 파츠를 재사용해 임시 제작 도우미 배치

## 의도

향후 프롬프트 기반 생산 파이프라인을 월드 안에서 이해할 수 있도록 서로 다른
임시 전문 캐릭터를 배치합니다. 이후 외형과 서비스를 교체해도 작성된 월드
데이터는 바꾸지 않는 구조를 유지합니다.

## 데이터 분류

- [ ] 계정 프로필
- [ ] 영속 작성 월드 데이터
- [ ] 런타임 관측
- [ ] 추론 상태
- [x] Unity 표현
- [x] 전송 / 권한 / 인프라

## 결정과 경계

8명의 전문 캐릭터는 소유자 전용 크리에이터 작업공간 표현 루트의 자식입니다.
외형은 캐릭터 파츠 카탈로그를 재사용하지만 플레이어 파츠 어댑터를 사용하거나
`equipped_part` Fact를 발행하거나 일반 월드 엔터티로 등록하지 않습니다.
런타임 게이트는 월드 입장 완료, 크리에이터 모드, canonical `owner` 권한을 모두
요구합니다.

## 온톨로지 표현

역할은 `WorldArchitect`, `ResourceMaker`, `OntologySteward`,
`PhysicsEngineer`, `RuleEngineer`, `QuestDesigner`, `UiDesigner`,
`QaPublisher` canonical service ID로 구분합니다. 한글 이름은 표시 전용입니다.
향후 각 서비스의 영속 결과는 기존 계정 또는 리비전된 World Authority 계약을
통과해야 합니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 활성 / 추가 | 입장한 월드 소유자가 서로 다른 파츠를 입은 도우미 8명을 확인 | `TormiaMain.unity`; `CreatorWorkspaceNPCsFinalView.png` |
| 비활성 / 제거 | 로그아웃, 입장 전, 크리에이터 모드 끔, editor, viewer 상태에서 작업공간 숨김 | `OntologyCreatorWorkspaceControllerTests` |
| 회귀 / 예외 | 외형 투영이 온톨로지 Actor나 월드 Fact를 만들지 않음 | NPC 하이라키에는 표현 컴포넌트와 복제 Visual만 존재 |

## 성능과 멀티플레이 영향

첫 슬라이스에는 폴링, DB 쓰기, Authority 명령, 네트워크 트래픽이 없습니다.
씬에 작성된 Visual 8개를 하나의 루트로 게이트합니다.

## 문서 갱신

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [ ] 언어팩 CSV / 마이그레이션 (필요 없음)
- [x] 테스트 시나리오 증거
