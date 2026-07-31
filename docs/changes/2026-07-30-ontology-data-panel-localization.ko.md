# 온톨로지 데이터 패널 언어팩 적용

## 요약

- **날짜:** 2026-07-30
- **담당:** TOV 개발
- **상태:** 구현 및 검증 완료
- **요청:** canonical 저장 ID를 번역하거나 오브젝트별 UI 예외를 만들지
  않고 온톨로지 데이터 패널 각 탭의 미번역 영어를 제거합니다.

## 의도

canonical 온톨로지 ID는 언어 중립 상태로 유지하면서 카탈로그에 등록된
관계, 값, 규칙 변수, 물리 선택지와 데이터 패널의 정적 컨트롤을 현재 표시
언어로 읽을 수 있게 합니다.

## 데이터 소유권

- `OntologyTerms.csv`가 canonical ID와 표시 키의 연결을 소유합니다.
- `Localization_en.csv`, `Localization_ko.csv`가 표시 문구와 읽기 쉬운
  팩트 문장 템플릿을 소유합니다.
- `OntologyRuntimeWorldFactEditorPanel`은 하이라키 작성 컨트롤을 언어팩
  키에 연결합니다. 한글 문구나 게임 의미를 직접 소유하지 않습니다.
- 확인된 시스템 생성 인스턴스 접미사는 식별자를 바꾸지 않고 카탈로그
  표시 키로 보여줍니다. 사용자 작성 이름과 자유 텍스트는 유지합니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 한글 또는 영문 활성화 | 정적 컨트롤, 지속 관계·값, 규칙 변수와 물리 선택지가 언어팩 키로 표시됨 | `OntologyWorldFactEditorLocalizationTests` |
| 항목 누락 또는 중복 | 영어 대체 문구를 조용히 출시하지 않고 CSV 검증이 실패함 | `OntologyLanguagePackServiceTests.ShippedCsvFilesValidateWithoutWarnings` |
| canonical 저장 | 표시 언어는 라벨만 바꾸며 저장 ID를 변경하지 않음 | 기존 언어팩 canonical ID 테스트 |
| 시스템 복제 이름 | `BeholderBasic_c267940a_Copy`는 `placeable.BeholderBasic` 표시 키로 보이고 사용자 작성 이름은 유지됨 | `OntologyLanguagePackServiceTests.EntityDisplayNameAndCanonicalTermCoverageContract` |

## 갱신 문서

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
