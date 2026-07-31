# Jua 프로젝트 타이포그래피

## 요약

- **날짜:** 2026-07-30
- **상태:** 구현 완료
- **범위:** 프로젝트 소유 Unity UI 표시

## 의도

하이라키 작성 UI와 런타임 생성 UI에 하나의 읽기 쉬운 TOV 게임 폰트를
적용하고, 사용자가 입력하거나 선택하는 텍스트를 조금 더 강조합니다.

## 결정

`Assets/UI/Fonts/Jua-Regular.ttf`에서 동적 TextMesh Pro 자산을 생성하고
TMP 프로젝트 기본 폰트로 지정합니다. `TormiaBootstrap`, `TormiaUI`,
`TormiaWorld`, `Assets/Prefabs/Ontology/UI`,
`Assets/Data/Ontology/UI`의 모든 TMP 텍스트가 이 자산을 사용합니다.

Unity `Selectable` 아래의 텍스트는 마이그레이션할 때 2포인트 커집니다.
그 밖의 라벨은 현재 크기를 유지합니다. 하이라키, 레이아웃, Transform,
정렬, 색상은 변경하지 않고 서드파티 자산은 제외합니다. 폰트가 바뀌는
시점에만 크기를 올리므로 다시 실행해도 안전합니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 프로젝트 작성 UI | 모든 TMP 컴포넌트가 Jua 사용 | `ProjectOwnedUiAssetsUseJua` |
| 상호작용 텍스트 | 글자 크기가 정확히 2포인트 증가 | `InteractiveTextGetsTwoPointIncreaseExactlyOnce` |
| 재실행 | 두 번째 실행은 크기를 추가로 변경하지 않음 | `InteractiveTextGetsTwoPointIncreaseExactlyOnce` |
| 비상호작용 라벨 | 기존 포인트 크기 유지 | `NonInteractiveTextKeepsItsExistingPointSize` |

## 갱신 문서

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
