# Authority 행동 애니메이션 의도

## 요약

- **날짜:** 2026-07-28
- **담당:** TOV 개발
- **상태:** 구현 및 검증 완료
- **관련 요청:** 몬스터 공격은 승인되지만 애니메이션이 바뀌지 않는 문제를
  컨트롤러 하드코딩 없이 해결

## 의도

승인된 버전형 행동이 콘텐츠 데이터의 canonical 애니메이션 의도를 선택하고,
배우의 등록 레퍼토리와 애니메이션 데이터베이스가 호환 클립을 선택합니다.

## 데이터 분류

- Unity 표현
- World Authority 콘텐츠 정의·투영
- 계정/월드 배우 레퍼토리 투영

## 결정과 경계

발행된 행동 정의가 `presentation.actorAnimationIntent`를 소유합니다.
World Authority가 이 메타데이터를 검증하고 투영합니다. Unity 입력 및 전투
컨트롤러는 ID 기반 행동 명령만 보내며 애니메이션을 선택하지 않습니다.
`OntologyAnimationAdapter`가 승인된 의도를 표현하며 영속
`animation_intent` Fact는 기록하지 않습니다.

## 온톨로지 표현

```text
published attack v5
  presentation.actorAnimationIntent = AttackLight

Player has_animation Anim_Sword_LightAttack
Anim_Sword_LightAttack intents AttackLight
```

표현 의도는 일시적인 명령 결과 메타데이터이며 영속 월드 Fact가 아닙니다.
공격 조건과 효과도 변경하지 않습니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 활성화 | 정확한 승인 행동 버전이 `AttackLight`를 해석하고 플레이어 레퍼토리가 검 공격 클립을 선택 | Unity EditMode 에셋·해석기 테스트 |
| 제거 | 표현 메타데이터가 없으면 애니메이션이 발생하지 않음 | Unity EditMode 해석기 테스트 |
| 거절·재전송 | 거절되거나 재전송 확인된 명령은 연출하지 않음 | 해석기 테스트 |
| 서버 검증 | 잘못된 canonical 표현 의도는 발행 거절 | World Authority 평가기 테스트 |
| Authority 전체 흐름 | 발행된 의도가 정확한 승인 행동 버전과 함께 투영되며 장착·공격·사망·재전송·비활성화·재접속 흐름도 유지 | `scripts/run-combat-authority-smoke.ps1` |

## 성능과 멀티플레이 영향

폴링, 프레임성 Fact, DB 쓰기를 추가하지 않았습니다. 이미 받은 행동 투영을
사용해 Authority 명령 완료 시 한 번만 해석합니다.

## 2026-07-31 배우 범위 강화

`CommandCompleted`는 클라이언트 전체 이벤트이므로, 이제 각 애니메이션
어댑터는 표현을 해석하거나 준비 큐에 넣기 전에 canonical 행동 실행
payload의 `actorEntityId`를 확인합니다. 다른 배우가 소유한 행동은 무시하고,
해당 행동을 소유한 배우만 투영 또는 시각 오브젝트가 준비될 때까지 승인된
표현을 재시도합니다. 잘못된 payload나 배우 ID가 없는 payload는 실패
폐쇄됩니다. 따라서 의도, 클립, 프리팹 또는 배우 이름 기반 fallback 없이
중복 `authority_animation_intent_unresolved` 만료 경고가 제거됩니다.

EditMode 회귀 검증은 소유 배우 경로, 비소유 배우 경로, 잘못된 payload 거절
경로를 함께 확인합니다.

## 갱신한 문서

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `docs/changes/2026-07-28-authority-action-animation-intent.md`
