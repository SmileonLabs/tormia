# 온톨로지 출처 소유권과 Authority 액션 메타데이터

## 요약

- **날짜:** 2026-07-23
- **담당:** 프로젝트 소유자와 Codex
- **상태:** 구현 및 검증 완료
- **관련 요청:** 전체 하드코딩 및 온톨로지 정책 위반 보완

## 의도

온톨로지 데이터를 제거하면 해당 행동도 제거되어야 합니다. 런타임 관측과
계정 소유 캐릭터 데이터는 영속 월드 상태가 되면 안 됩니다. 공유 월드 액션은
World Authority가 선택한 패키지 메타데이터를 사용해야 합니다.

## 데이터 분류

- 계정 프로필
- 영속 작성 월드 데이터
- 런타임 관측
- 추론 상태
- 전송 / 권한

## 결정과 경계

`OntologyWorldState`는 Fact마다 출처별 기여를 기록합니다. 한 출처를 제거해도
동일 트리플을 소유한 다른 출처의 기여는 남습니다. 로컬 스냅샷에는 영속 출처
Fact만 포함합니다. 실행 카탈로그가 비어 있으면 그대로 비어 있으며 일치하지
않는 verb는 거부합니다.

개발 패키지 발행 데이터는 `WorldAuthoritySettings`에 둡니다. 서버 월드
projection은 활성 액션의 패키지·버전·정의 버전을 제공하고 Unity는 이
메타데이터로 `execute_action`을 전송합니다.

Authority canonical 의미 ID는 ASCII 영문 식별자만 허용합니다. 번역 표시는
별칭과 UI 데이터로만 유지합니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 명시적 액션 정의 | 후보와 효과 실행 | Unity 코어 액션 테스트 |
| 빈/제거된 액션 카탈로그 | 숨은 기본 행동과 임의 Fact 없음 | `EmptyActionCatalogDoesNotRestoreHiddenDefaults` |
| 겹치는 Fact 출처 | 프로필/외형 제거 후 영속 Fact 유지 | WorldState와 projector 테스트 |
| 스냅샷 | 관측·프로필·외형·추론 제외 | 저장/복원 테스트 |
| Authority 액션 | projection 메타데이터로 패키지/버전 선택 | Authority 빌드와 Unity 테스트 |

2026-07-23 최종 검증 결과:

- Unity EditMode: 66개 통과, 실패 0개
- Unity PlayMode: 37개 통과, 실패 0개
- `scripts/verify-development.ps1 -RequireServices`: 통과
- 컴파일 및 테스트 후 Unity Console: 오류 0개
- 검증된 이미지로 로컬 World Authority 컨테이너 재생성, 상태: `healthy`

## 성능과 멀티플레이 영향

Fact별 출처 집합에 작은 메모리 비용이 추가됩니다. 대신 다른 소유자의 Fact를
잘못 제거하거나 임시 데이터를 저장하는 문제를 막습니다. 월드 projection에는
작은 활성 액션 메타데이터 목록만 추가되며 프레임별 DB 쓰기는 없습니다.

## 갱신한 문서

- `PROJECT_CONTEXT.md`
- `PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
