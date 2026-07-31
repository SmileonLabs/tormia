# Authority 투영 기반 장착 애니메이션 상태

## 요약

- **날짜:** 2026-07-28
- **상태:** 구현 및 검증 완료
- **범위:** World Authority 투영, Unity 애니메이션 표현, 전투 데이터

## 의도

장착한 배우는 장착 아이템의 표현 데이터가 선언한 정지·이동 자세를 사용해야
합니다. 런타임 코드는 무기명·프리팹명·메시명·GameObject 이름으로
`WeaponIdle`이나 `WeaponWalk`을 선택하지 않습니다.

## 소유권 결정

- `equipped_item`은 계속 World Authority가 소유하는 영속·리비전 데이터입니다.
- 장착 엔티티의 투영된 `templateId`가 명시적인 `CombatCatalog` 표현 정의를
  선택합니다.
- 선택된 Idle/Move 의도와 Animator/Playable 상태는 일시적인 Unity 표현입니다.
- 정지 장착 표현을 위해 중복 영속 애니메이션 Fact를 기록하지 않습니다.

## 활성화 및 제거 동작

| 경우 | 기대 결과 |
| --- | --- |
| 배우에게 하나의 `equipped_item`이 있고 템플릿에 Idle 의도가 명시됨 | 정지 중 해당 의도를 반복 재생 |
| 같은 정의에 Move 의도가 있고 배우가 이동함 | `WeaponWalk` 의도로 `WeaponeWalk.fbx`를 반복 재생 |
| 승인된 일시 행동이 재생됨 | 행동이 자세를 잠시 덮고 완료 후 현재 이동 상태에 맞는 장착 Idle/Walk로 복귀 |
| 장착 관계·카탈로그 정의·Idle/Move 의도 중 하나가 제거됨 | 숨은 무기 애니메이션 fallback 없이 Controller Idle/Locomotion 복귀 |
| 투영에 서로 다른 장착 엔티티가 충돌함 | 임의의 장착 자세를 선택하지 않음 |

## 데이터와 버전

개발 전투 패키지를 `1.3.0`으로 올립니다. `equip_weapon` 정의 버전 3은
`presentation.actorAnimationIntent=WeaponEquip`을 선언하며, 영속 효과는
계속 Authority 소유 `equipped_item` 관계입니다.

## 검증

- `OntologyCombatVerticalSliceAssetTests` 16/16 통과: 활성·제거·반복 Idle 및
  Humanoid `WeaponWalk` 경우를 포함합니다.
- `OntologyAttachmentAdapterTests` PlayMode 3/3 통과.
- 컴파일 및 런타임 시작 점검 후 Unity Console 오류 0개.
- `scripts/verify-development.ps1` 통과: Docker World Authority 빌드 포함.
- 로그아웃 상태 런타임에서는 플레이어 표현이 비활성인 것이 정상임을
  확인했습니다. 로그인 후 실제 화면 최종 확인은 수동 게임플레이 점검으로
  남깁니다.

## 갱신 문서

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
