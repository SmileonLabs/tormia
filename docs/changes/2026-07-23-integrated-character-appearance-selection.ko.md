# 캐릭터 선택·외형 확인 통합

## 요약

- **날짜:** 2026-07-23
- **상태:** 구현 및 검증 완료
- **분류:** Unity 표현과 계정 진입 화면 이동

## 의도와 경계

`AccountCharacterSelectionPanel`이 기존 계정 캐릭터의 최종 외형 확인 역할을
함께 담당합니다. 선택 캐릭터 상세 영역에서 전신 미리보기와 현재 착용 파츠를
확인한 뒤 다음 단계로 진행합니다.

별도 `AccountAppearanceReviewPanel`은 활성 씬과 가입·진입 흐름에서 제거합니다.
캐릭터 선택 화면의 계속 버튼은 월드 선택 화면으로 바로 이동합니다. 이전 씬
이벤트가 남아 있어도 제거된 화면을 다시 열지 않도록 호환 메서드
`ShowAppearanceReview()` 역시 월드 선택으로 연결합니다.

계정 소유 외형 데이터, 월드 Fact, Authority 명령은 변경하지 않습니다. 중복된
표현 단계만 제거합니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 통합 흐름 | 캐릭터 선택 화면에서 선택 캐릭터와 착용 파츠 아이콘 확인 | `character_selection_v2_final_verified.png` |
| 계속 | 계속 버튼을 누르면 월드 선택으로 바로 이동 | `OntologyAccountCharacterSelectionPanel`의 `ShowWorldSelection()` 연결 |
| 제거된 동작 | 별도 외형 확인 패널이 표시되지 않음 | `AppearanceRouteSkipsRemovedPanelAndOpensWorldSelection` |
| 호환 경로 | 이전 외형 확인 호출도 월드 선택으로 진행 | `ShowAppearanceReview()`가 `ShowWorldSelection()`에 위임 |
| 회귀 | 계정 화면 이동 테스트와 개발 하네스 통과 | 관련 PlayMode 테스트와 `verify-development.ps1` |

## 성능과 권한 영향

폴링, 데이터베이스 쓰기, 영속 월드 이벤트, 새로운 Authority 호출은 추가하지
않았습니다. 중복 UI 전환과 미리보기 스튜디오 하나를 제거했습니다.
