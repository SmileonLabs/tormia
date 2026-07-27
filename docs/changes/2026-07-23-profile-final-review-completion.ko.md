# 프로필 최종 확인 화면 완성

## 요약

- **날짜:** 2026-07-23
- **상태:** 구현 및 검증 완료
- **분류:** 계정 프로필 표현, Authority 기반 월드 권한, Unity 화면 이동

## 의도와 경계

`AccountProfileReviewPanel`은 월드 입장 전 마지막 확인 단계입니다. 선택 캐릭터,
착용 외형, 선택 월드 이름, 계정 프로필 관계, Authority가 제공한 월드 역할,
편집 가능 여부와 입장 준비 상태를 표시합니다.

월드 권한은 `SelectedWorldRole`과 `CanEditSelectedWorld`에서 읽습니다. UI가
권한을 추론하거나 부여하지 않습니다. 소유자와 편집자는 월드 편집 가능으로,
열람자는 읽기 전용으로 표시합니다. 역할이 없으면 월드 입장 버튼을 활성화하지
않습니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 소유자·편집자 | 현지화된 역할과 월드 편집 가능 표시, 입장 활성화 | `ProfileFinalReviewShowsAuthorityRoleAndRequiresWorldPermission` |
| 열람자 | 열람자와 읽기 전용 표시, 월드 입장은 허용 | `ProfileFinalReviewShowsAuthorityRoleAndRequiresWorldPermission` |
| 권한 누락 | 권한 없음 표시와 입장 비활성화 | `ProfileFinalReviewShowsAuthorityRoleAndRequiresWorldPermission` |
| 착용 외형 | 하이라키 기반 슬롯 4개가 직렬화되고 `CharacterPartDatabase`와 연결 | `AccountProfileReviewPanel.appearanceSlots`의 참조 4개 확인 |
| 화면 이동 | 월드 선택에서 최종 확인으로 이동하고 입장 시 로딩으로 진행 | `OntologyAccountFlowNavigatorTests` |
| 로딩 | 진행 중에도 진행 바 이미지 비율 유지 | `OntologyWorldEntryLoadingPanelTests` |
| 회귀 | 개발 하네스와 World Authority Docker 빌드 통과 | `scripts/verify-development.ps1` |

## 성능과 권한 영향

World Authority가 이미 반환한 계정 대시보드만 읽습니다. 추가 요청, 폴링,
영속 이벤트, 데이터베이스 쓰기 또는 숨은 권한 대체 규칙은 추가하지 않았습니다.
