# 단일 점프와 착지 전환

## 요약

- **날짜:** 2026-07-31
- **담당:** TOV 팀
- **상태:** 구현
- **관련 요청:** 착지할 때 플레이어가 한 번 더 점프하는 것처럼 보이는 문제

## 의도

Authority가 승인한 입력 엣지 하나에서 물리 점프와 애니메이션 발생이 각각 한
번만 만들어지게 하고, 충돌 접지 뒤까지 공중 포즈가 남지 않게 합니다.

## 데이터 분류

- [ ] 계정 프로필
- [ ] 지속 작성 월드 데이터
- [x] 런타임 관찰
- [x] 추론 상태
- [x] Unity 표현
- [x] 전송 / Authority / 인프라

## 결정과 경계

원시 점프 입력은 중복을 합치는 단일 소비 방식의 임시 엣지입니다. 전송 준비가
되면 intent sender가 한 번만 소비합니다. 충돌과 애니메이션 어댑터는 나중에
도착한 Authority 승인 표현 엣지만 받습니다.

애니메이션 전환 소유권은 Manifest에 유지됩니다. `canBlend=true`는 정규화된
믹서에서 크로스페이드하고, `canBlend=false`는 즉시 전환합니다. 어댑터가 이
작성 값을 더 이상 무시하지 않습니다. 현재 Landing 항목은 이미 혼합 불가로
작성되어 있으므로 CharacterController가 접촉을 보고하는 즉시 Airborne 포즈를
교체합니다. 클립 이름이나 캐릭터 이름 예외는 추가하지 않았습니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 입력 활성 | 하나의 대기 엣지는 한 번만 소비되어 최대 한 번만 제출됩니다. | `EphemeralJumpIntentCanBeConsumedOnlyOnce` |
| 착지 활성 | 혼합 불가 Landing 항목이 Airborne 믹서 가중치를 즉시 제거합니다. | `NonBlendableLandingCutsAirbornePoseImmediately` |
| 규칙 제거 | `jump_action` 또는 배정된 규칙 블록이 없으면 숨은 Unity 점프가 만들어지지 않습니다. | `RemovingJumpActionFactRemovesUnityIntentRoute`; `RemovingPlayerJumpRuleBlockRemovesJumpBehavior` |

## 갱신 문서

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] 테스트 시나리오 목록
