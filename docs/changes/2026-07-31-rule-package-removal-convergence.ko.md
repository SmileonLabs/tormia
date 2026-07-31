# 규칙 패키지 제거 수렴성 복구

## 요약

- **날짜:** 2026-07-31
- **상태:** 구현 및 검증 완료
- **범위:** 의미 계약 버전 판독, 기본 의미 패키지 발행, Rule Block 연속 제거,
  World Authority 멱등성

## 문제

숫자형 `semantic_contract_version` Fact를 Unity가 문자열 canonical ID에서만
읽고 있었습니다. 따라서 이미 최신 계약인 몬스터도 버전 0으로 오인되어
월드 로드 때마다 `template_semantic_baseline` 패키지가 다시 적용됐습니다.
사용자가 사냥 중 기본 Rule Block을 제거해도 다음 투영 갱신에서 기본
트리플과 규칙이 되살아날 수 있었습니다.

또한 UI에서 여러 Rule Block을 빠르게 지우면 첫 요청이 패키지 전체를
철회한 뒤, 이미 전송 중이던 형제 바인딩 삭제 요청이 `not found` 또는 오래된
리비전으로 돌아와 Unity의 낙관적 삭제를 되돌릴 수 있었습니다.

## 결정과 경계

- 의미 계약 버전은 투영 Fact의 타입화된 `objectValueJson`을 우선 읽고,
  레거시 문자열 Fact만 `objectCanonicalId`로 보완합니다.
- 템플릿 기준 패키지의 application ID는 엔티티·슬롯·불변 package ID로부터
  결정적으로 생성합니다. 동일 패키지 재전송은 새로운 의미 기여분을 만들지
  않습니다.
- Authority는 같은 application/package 재적용을 `unchanged` 성공으로,
  이미 철회된 기존 바인딩의 재삭제를 멱등 성공으로 처리합니다.
- Unity의 의미 패키지와 Rule Block 명령은 리비전 충돌 시 최신 투영을 받은
  뒤 재시도하며, 이미 목표 상태이면 로컬 행을 복구하지 않습니다.
- 패키지 소유 Rule Block 하나를 제거하면 그 패키지가 소유한 트리플·프로필
  선언·형제 Rule Block이 원자적으로 함께 철회됩니다. 사용자가 별도로 작성한
  독립 데이터는 삭제하지 않습니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 숫자 버전 | 숫자형 계약 버전 7을 7로 읽고 레거시 값으로 잘못 낮추지 않음 | `PlacedSemanticContractReadsTypedNumericProjectionFact` |
| 반복 적용 | 동일 application/package 재전송이 활성 기여분을 교체하거나 중복 생성하지 않음 | 의미 패키지 Authority 스모크 |
| 연속 삭제 | 첫 삭제가 패키지를 제거한 뒤 같은 바인딩 삭제가 늦게 도착해도 성공으로 수렴 | 의미 패키지 Authority 스모크 |
| 제거 보존 | 바인딩은 개별 철회하고 마지막 바인딩에서 패키지 소유 데이터를 정리하며 독립 작성 데이터는 유지 | `RemovingPreconfiguredRulesCompletesOwnedBaselineOnlyAtLastRule` |

## 성능과 멀티플레이 영향

수정은 명령·투영 경계에서만 동작합니다. 프레임별 쓰기나 Unity 소유 규칙
평가를 추가하지 않습니다. 결정적 ID와 멱등 삭제는 재전송 및 여러 클라이언트
투영 갱신 시 같은 Authority 상태로 수렴하게 합니다.
