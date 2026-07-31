# 룰 표현 전송 호환성

## 요약

- **날짜:** 2026-07-29
- **담당:** TOV 개발 하네스
- **상태:** 구현 및 검증 완료
- **관련 이슈:** `rule_definition_version_conflict:BuoyantWhenInWater:2`로
  월드 입장이 중단됨

## 의도

선택적 룰블록 런타임 표현을 추가하면서, 이를 사용하지 않는 모든 기존
룰블록의 불변 식별값이 바뀌지 않게 합니다.

## 데이터 분류

- [x] 영속 작성 월드 데이터
- [ ] 런타임 관측
- [ ] 추론 상태
- [x] Unity 표현
- [x] 전송 / 권한 / 인프라

## 결정과 경계

Unity 직렬화는 표현을 작성하지 않은 룰에도 빈 `runtimePresentation`
오브젝트를 보낼 수 있습니다. World Authority canonicalization은 체크섬 비교
전에 이 빈 스키마 자리표시자만 제거합니다. 따라서 해당 필드가 없었던 기존
payload와 전송상 동일하게 취급됩니다.

비어 있지 않은 런타임 표현은 계속 불변 룰 콘텐츠입니다. 정상적으로 저장·
해시·버전 관리합니다. 기존 데이터베이스 행을 삭제하거나 다시 쓰지 않습니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 기존 / 빈 값 | 필드 부재와 Unity 빈 표현이 같은 canonical payload가 된다 | `EmptyRuntimePresentationPreservesLegacyImmutablePayload` |
| 설정됨 | `AttackLight`가 canonical 불변 payload에 유지된다 | `ConfiguredRuntimePresentationRemainsImmutableRuleContent` |
| 기존 실제 월드 | `BuoyantWhenInWater v2` 체크섬을 유지하고 새 휘두르기 룰 발행·패키지 `3.0.0` 활성화·입장에 성공한다 | 2026-07-29 실제 패키지 호환 프로브 |

2026-07-29 검증 결과:

- World Authority 테스트: 40개 통과
- 기존 계정·월드 패키지 발행: 통과
- 월드 패키지 `3.0.0`: 활성화
- 월드 입장: 성공

## 갱신한 문서

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] 테스트 시나리오 목록
