# 단일 Unity UI 원본 씬

## 요약

- **날짜:** 2026-07-26
- **담당:** TOV 팀
- **상태:** 구현
- **관련 요청:** `TormiaMain`의 중복 UI 소유 제거

## 의도

분리된 Bootstrap/World/UI 런타임 구성을 유지하면서 계정 및 게임 내부 UI의
수정 가능한 원본을 `TormiaUI` 하나로 통일합니다.

## 데이터 분류

- [ ] 계정 프로필
- [ ] 영속 작성 월드 데이터
- [ ] 런타임 관측
- [ ] 추론 상태
- [x] Unity 표현
- [ ] 전송 / 권한 / 인프라

## 결정과 경계

`TormiaUI`만 UI 작성 원본 씬으로 사용합니다. 빈 레거시
`FarmUI_DemoCanvas`와 기존 `MainMenuController` 동작을 이 씬에서 제거했습니다.
통합 `TormiaMain` 씬은 `Assets/Scenes/Legacy/TormiaMain.unity`로 이동했고,
이 씬에서 분리 씬을 다시 만드는 파괴적 도구도 제거했습니다.

빌드 구성은 계속 `TormiaBootstrap`, `TormiaWorld`, `TormiaUI`를 사용합니다.
보관 씬은 복구 가능한 과거 자료일 뿐이며 빌드·테스트·작성 권한을 다시
가지면 안 됩니다.

## 온톨로지 표현

온톨로지 용어, Fact, 규칙, 프로필, Authority 명령은 변경하지 않습니다.
이 작업은 Unity 표현 소유권 정리입니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 활성 / 추가 | Bootstrap이 원본 `TormiaUI` 하이라키를 로드 | `TormiaUiSceneSmokeTests` |
| 비활성 / 제거 | 구성된 런타임에 `FarmUI_DemoCanvas`가 존재하지 않음 | `TormiaUiSceneSmokeTests.CompositionCreatesOntologyWorldAndUsesSingleUiScene` |
| 회귀 / 예외 | 계정·퀘스트·꾸미기·HUD·토스트 연결이 계속 정상 | 같은 PlayMode 모음과 기존 씬 구성 테스트 |

## 성능과 멀티플레이 영향

빈 Canvas, raycaster, 레거시 컨트롤러 한 세트를 제거합니다. 네트워크, DB,
Authority, 온톨로지 런타임에는 영향이 없습니다.

## 문서 갱신

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [ ] 언어팩 CSV / 마이그레이션 (필요 없음)
- [x] 관련 테스트
