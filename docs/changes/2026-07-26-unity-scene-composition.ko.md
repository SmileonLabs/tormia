# Unity 부트스트랩·월드·UI 씬 구성

## 요약

- **날짜:** 2026-07-26
- **담당:** Tormia 팀
- **상태:** 구현 및 검증 완료
- **관련 요청:** 유지보수를 위해 후보정 가능한 UI를 월드 씬에서 분리

## 의도

기존 계정→월드 흐름, 온톨로지 소유권 경계, 수정 가능한 Unity 하이라키를
유지하면서 UI 작성과 3D 월드 작성을 서로 독립적으로 관리합니다.

## 데이터 분류

- [ ] 계정 프로필
- [ ] 영속 작성 월드 데이터
- [ ] 런타임 관측
- [ ] 추론 상태
- [x] Unity 표현
- [x] 전송 / 권한 / 인프라

## 결정과 경계

빌드는 `TormiaBootstrap`에서 시작합니다. 영속 서비스와 `EventSystem`은 이
씬에 둡니다. `TormiaWorld`와 `TormiaUI`는 Additive로 로드되며 각각 3D 표현과
UI 표현의 직접 편집 원본입니다. 예전 `TormiaMain` 통합 스냅샷은
`Assets/Scenes/Legacy` 아래에 보관하며 작성·테스트·빌드 씬으로 사용하지
않습니다.

기존 에디터 마이그레이션 명령은 제거했습니다. 분리된 월드·UI 씬의 직접 작성
내용을 덮어쓸 수 있으므로 Legacy 스냅샷에서 분리 씬을 다시 생성하지 않습니다.

씬 분리는 계정 데이터를 월드 Fact로 옮기거나 게임플레이 규칙을 UI로 옮기지
않습니다. 로더는 Additive 활성화를 기다리고, 씬 간 컨트롤러 연결을 갱신한 뒤
로컬 작성 투영을 다시 구성합니다.

## 온톨로지 표현

canonical 관계, 프로필, 규칙, 규칙 블록, Fact 출처는 바뀌지 않습니다. 씬
경계는 Unity 표현 및 배포 경계일 뿐입니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 활성 / 추가 | Bootstrap이 분리된 World와 UI를 로드하고 작성된 Fact를 사용할 수 있음 | `TormiaSceneCompositionSmokeTests.BootstrapLoadsWorldAndUiAsSeparateScenes`, `SceneCompositionSignedOutFinal.png` |
| 비활성 / 제거 | 로그아웃 상태에서는 월드 HUD와 런타임 표현이 계속 숨겨짐 | 같은 스모크 테스트와 기존 런타임 게이트 테스트 |
| 회귀 / 예외 | 구성 완료 후 EventSystem과 AudioListener가 각각 하나만 존재 | 같은 스모크 테스트 |

## 성능과 멀티플레이 영향

두 Additive 씬은 시작 시 한 번 로드됩니다. 프레임별 입력과 관측은 바뀌지
않습니다. Authority만 영속 공유 월드 쓰기 경계로 유지됩니다.

## 문서 갱신

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [ ] 언어팩 CSV / 마이그레이션 (필요 없음)
- [x] 테스트 시나리오 증거
