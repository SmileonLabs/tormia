# 패키지 범위 행동 정의와 격리 Authority 스모크

## 요약

- **날짜:** 2026-07-28
- **담당:** TOV 개발 하네스
- **상태:** 구현 및 검증
- **관련 문제:** 새 패키지 버전 발행 시
  `action_definition_version_conflict`로 월드 입장이 중단됨

## 의도

Authority 실행·투영에서 이미 사용하는 정확한 행동 식별자와 DB 식별자를
일치시킵니다. 회귀 테스트가 공유 로컬 DB에 계정·월드·패키지·정의를 남기지
않도록 합니다.

## 데이터 분류와 경계

- 영속 콘텐츠: 패키지 범위 불변 Authority 행동 정의
- 영속 월드 데이터: 리비전 명령으로 활성화한 패키지 ID·버전
- 테스트 데이터: 격리 스모크 DB 내부의 일회성 계정·월드·엔티티·팩트·패키지
- Unity 표현: 계정 흐름의 현지화 상태. 원시 기술 정보는 Console에만 기록

## 결정

`action_effect`의 고유성은
`packageId + packageVersion + actionId + definitionVersion`입니다. 현재 규칙
바인딩에는 패키지 식별자가 없으므로 행동이 아닌 콘텐츠는 전역 ID·버전
고유성을 유지합니다.

일반 전투 스모크는 전용 Compose 네트워크와 볼륨을 만들고 실행 후 제거합니다.
영속 로컬 서비스를 사용하려면 `-UseExistingServices`를 명시해야 합니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 패키지 간 재사용 | 같은 행동 ID·버전을 서로 다른 패키지 식별자로 발행할 수 있다. | 격리 Authority 전투 스모크 |
| 동일 조합 변경 | 같은 전체 식별자에 다른 내용을 발행하면 계속 충돌한다. | 저장소 체크섬 검사 |
| 활성화 | 패키지 1.4.0이 발행되고 선택 캐릭터가 월드에 입장한다. | 실제 Unity·Authority 입장 검증 |
| 제거 | 비활성 패키지는 실행을 거절하고 격리 스모크는 Docker 볼륨을 남기지 않는다. | 격리 Authority 전투 스모크 |
| 표현 | 원시 거절 코드는 Console에만 남고 UI는 현지화 안내를 표시한다. | Unity EditMode 언어팩 키 테스트 |

## 갱신 문서

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `infrastructure/README.md`
- `tests/harness/core-regression-scenarios.json`
