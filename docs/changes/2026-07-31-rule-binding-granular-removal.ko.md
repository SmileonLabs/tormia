# 의미 패키지 Rule Block 개별 제거

## 요약

- **날짜:** 2026-07-31
- **상태:** 구현 완료, 개발 하네스 검증 기록
- **범위:** World Authority 의미 패키지 소유권, 런타임 에디터 대체 경로,
  typed 트리플 소유권, Rule Block 제거

## 문제

의미 패키지가 소유한 Rule Block 하나를 제거하면 형제 Rule Block까지 모두
삭제되었습니다. 동시에 템플릿 기준 패키지는 canonical Fact만 소유했기 때문에
패키지를 제거해도 체력과 튜닝 같은 number·boolean 트리플이 남았습니다.

## 결정과 경계

- 패키지 소유 Rule Block은 하나씩 제거할 수 있습니다.
- 제거 시 해당 바인딩과 그 `RuleBound` 결과만 철회합니다.
- 다른 패키지 소유 바인딩이 남아 있으면 공유 작성 의미를 유지합니다.
- 마지막 소유 바인딩을 제거하면 패키지를 종료하고, 패키지가 소유한 canonical
  및 typed Fact를 모두 철회한 뒤 밀려났던 작성 값을 복원합니다.
- 독립 작성 Fact와 다른 패키지가 소유한 기여는 유지합니다.
- Unity 로컬 대체 경로도 마지막 바인딩 정리 규칙을 따릅니다. 정상 연결
  플레이에서는 여전히 Authority가 게임 동작을 결정합니다.
- 과거 패키지가 소유하지 않았던 기존 Fact를 이름으로 추측해 삭제하지
  않습니다. 새 기준 및 마이그레이션 기준은 typed Fact를 명시적으로 소유합니다.

## 검증

| 경우 | 기대 결과 | 근거 |
| --- | --- | --- |
| 중간 제거 | 선택한 바인딩만 사라지고 형제 바인딩과 공유 의미 유지 | `RemovingPreconfiguredRulesCompletesOwnedBaselineOnlyAtLastRule`, Authority 의미 패키지 스모크 |
| 마지막 제거 | 마지막 바인딩이 패키지를 종료하고 소유 트리플·프로필 철회 | PlayMode 마지막 제거 검증, Authority 의미 패키지 스모크 |
| typed 소유권 | number·boolean Fact의 object kind가 패키지 payload에 보존 | `MeaningPackagePreservesTypedAuthoredFacts` |
| 보존 | 독립 데이터와 다른 패키지 기여 유지 | PlayMode 독립 데이터 검증 |

