# 엔티티 표시 이름과 canonical 용어 커버리지

## 요약

- **날짜:** 2026-07-30
- **상태:** 구현 완료, 아래 검증 기록 참조
- **범위:** Unity 표시, 온톨로지 언어 레지스트리, 에디터 저작 도구,
  개발 하네스

## 의도

Authority와 진단에 필요한 안정적인 월드 식별자는 유지하면서 사용자에게는
읽기 쉬운 다국어 이름을 보여줍니다. canonical 컨셉 등록 누락이 영문 ID
노출로 조용히 넘어가지 않고 개발 검증 실패로 발견되게 합니다.

## 데이터 분류

- 영속 엔티티 ID와 저장 인스턴스 이름: 변경 없는 월드 데이터
- 다국어 기본 이름과 중복 순번: Unity 표시 전용
- canonical 컨셉 등록: 작성된 온톨로지 어휘
- 에디터 파일명 제안: 런타임과 무관한 저작 보조 기능

## 결정과 경계

`OntologyEntityDisplayNameResolver`는 표시 단계에서 확인된 시스템 생성
접미사만 제거합니다. 숫자 배치 순번과 8자리 Authority GUID 접두부만
인식하며, 임의 접미사와 사용자가 작성한 이름은 그대로 유지합니다. 저장
식별자, Fact, Authority 명령은 변경하지 않습니다.

`Damageable`, `Monster`, `Item`, `Sword`, `Weapon`을 영문·한글 라벨과
별칭을 가진 canonical Concept로 등록했습니다. 프로젝트 커버리지 검사는
프로필, 템플릿, 룰 블록 프리셋, 규칙 조건/효과, 카탈로그 의미
마이그레이션을 역참조하여 누락되었거나 잘못된 종류로 등록된 모든 컨셉을
보고합니다.

카탈로그 에디터는 프리팹 파일명으로 비어 있는 표시 메타데이터를 제안할 수
있지만 빈 값만 채우며 게임 의미를 지정하지 않습니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 시스템 생성 엔티티 이름 | GUID/순번 없이 다국어 기본 이름 표시 | `EntityDisplayNameAndCanonicalTermCoverageContract` |
| 사용자 작성 이름 | 입력한 이름을 정확히 유지 | `EntityDisplayNameAndCanonicalTermCoverageContract` |
| 미등록 컨셉 | 커버리지 도우미가 누락 canonical Concept 보고 | `MissingCanonicalConceptReferenceFailsCoverageValidation` |
| 등록된 전투 컨셉 | canonical ID를 바꾸지 않고 영문·한글 라벨 해석 | `RegisteredCombatConceptsHaveLocalizedLabels` |

최종 Unity 테스트와 하네스 결과는 완료 보고에 기록합니다.

## 갱신 문서

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
- 온톨로지 언어 CSV 파일
