# 게임 내부 캐릭터 꾸미기 UI

## 요약

- **날짜:** 2026-07-23
- **상태:** 구현
- **분류:** 계정 프로필 + 런타임 관찰 + Unity 표현

## 결정

게임 내부 `OntologyCharacterCustomizationPanel`은
`AccountCharacterAppearancePanel`과 분리해서 유지합니다. 게임 내부 패널만 C키와
`ToggleHint` 버튼을 소유합니다. 두 패널은 같은 카테고리 아이콘, 원형 파츠 카드,
실시간 3D 미리보기 표현을 재사용하지만 화면 이동과 생명주기는 분리합니다.

파츠를 선택하면 로컬 어댑터와 투영된 `equipped_part` Fact가 즉시 갱신됩니다. 게임 내부
패널을 닫으면 선택 파츠 ID를 World Authority를 통해 현재 선택된 계정 캐릭터에
저장합니다. 지속 데이터의 주인은 계정 프로필이며, 선호 외형을 기억하기 위한 새로운
월드 Fact는 만들지 않습니다.

## 켜진 경우와 제거된 경우

| 경우 | 기대 결과 |
| --- | --- |
| 게임 내부 패널 사용 | C키/열기 버튼은 게임 내부 패널만 열고, 파츠 클릭 즉시 장착하며, 닫을 때 계정 외형을 저장 |
| 가입 외형 화면 사용 | 계정 흐름을 통해서만 열리고 게임 내부 C키/열기 버튼을 소비하지 않음 |
| 게임 내부 패널 제거 또는 비활성 | 가입용 패널이 숨은 대체 입력으로 외형 UI를 열지 않음 |

## 검증

- 씬 연결과 하이라키는 `TormiaMainSceneSmokeTests`로 확인합니다.
- 파츠 충돌과 결합 의상 동작은 `OntologyCharacterPartAdapterTests`로 확인합니다.
- 공유 하네스와 World Authority 서비스는
  `scripts/verify-development.ps1 -RequireServices`로 확인합니다.
