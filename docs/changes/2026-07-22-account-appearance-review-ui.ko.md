# 계정 외형 확인 UI

## 요약

- **결정:** 개발용 입장 흐름에서 외형 확인과 계정 프로필 온톨로지 확인을 분리합니다.
- **범위:** 기존 계정 소유 캐릭터 데이터를 보여 주는 Unity 표현 계층입니다.

## 흐름

```text
개발 계정 -> 캐릭터 선택 -> 외형 확인
-> 월드 선택 -> 프로필 온톨로지 확인 -> 월드 입장
```

`OntologyAccountAppearanceReviewPanel`은 선택된 계정 캐릭터의 이름,
템플릿, 장착 파츠 ID만 읽습니다. 월드 Fact를 쓰거나 런타임에 시각 UI를
생성하지 않습니다. 패널은 `OntologyGameCanvas` 아래의 하이라키 기반
오브젝트이며 기존 Farm UI 스타일 패널을 복제해 만들었으므로, 디자이너가
Unity에서 직접 배치와 디자인을 조정할 수 있습니다.

## 검증

- Play Mode에서 navigator가 새 외형 확인 패널을 여는 것을 확인했습니다.
- `TormiaMain` 씬에 외형 패널, 계정 입장 흐름, CanvasGroup, 요약 라벨,
  이동 버튼, navigator 참조를 직렬화해 이 단계가 런타임 검색에 의존하지
  않도록 했습니다.
- `OntologyAccountFlowNavigatorTests.AppearanceStepOpensOnlyWhenPanelIsPresent`
  Play Mode 테스트가 통과했으며 패널이 있는 경우와 제거된 경우를 모두
  검증합니다.
- `scripts/verify-development.ps1 -SkipServerBuild`가 통과했습니다.
- 패널과 navigator 코드 컴파일 직후 Unity Console에 오류가 없었습니다.
  이후 MCP 재연결 이슈는 별도로 추적하며 게임플레이 또는 계정 데이터에는 영향을 주지 않습니다.
