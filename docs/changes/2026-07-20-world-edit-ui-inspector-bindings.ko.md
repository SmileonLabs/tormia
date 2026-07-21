# 월드 편집 UI 인스펙터 연결값

## 결정

하이라키에서 만드는 월드 편집 UI는 이제 게임 실행 중에
`Transform.Find(...)`로 루트 컨트롤을 찾지 않습니다.
`OntologyRuntimeWorldFactEditorPanel`과 `OntologyRuntimeWorldEditorPanel`은
`OntologyGameCanvas`에 저장된 인스펙터 연결값을 사용합니다.

## 이유

월드 편집 화면은 Farm UI 키트를 기반으로 Unity 하이라키에서 직접
수정하는 것이 원칙입니다. 실행 중 이름 경로를 찾는 방식은 레이아웃을
조금 수정하거나 이름을 바꾸는 것만으로도 버튼 연결을 조용히 망가뜨릴 수
있었습니다. 또한 연결값이 매번 덮어써져 인스펙터가 실제 상태를 보여주지
못했습니다.

## 이관과 범위

두 패널에는 한 번만 쓰는 **Migrate Legacy Hierarchy Bindings** 컨텍스트
메뉴가 있습니다. 이전 씬의 연결값을 채우기 위한 Unity Editor 전용 기능이며,
게임 실행 중에는 호출되지 않습니다. 현재 `TormiaMain` 씬은 이관 후 저장되어
있습니다.

이번 작업은 패널과 루트 컨트롤의 정적 연결을 대상으로 합니다. 반복해서
생성되는 행 템플릿 내부의 자식 컨트롤 연결은 다음 별도 단계에서 이관합니다.

## 검증

- `OntologyRuntimeWorldFactEditorPanel`의 월드 편집 연결값 24개가 모두 할당됨.
- Unity 컴파일 뒤 프로젝트 코드 오류 없음.
- `scripts/verify-development.ps1 -SkipServerBuild` 통과.
- `OntologyWorldStateTests` EditMode 4/4 통과.

