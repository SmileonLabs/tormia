# 플레이어 점프 런타임 제거

## 요약

- **날짜:** 2026-07-31
- **담당:** TOV 개발 하네스
- **상태:** 구현
- **관련 요청:** 현재 플레이어 점프 코드를 제거한 뒤 기능을 다시 설계

## 의도

현재 플레이어 점프 경로를 하나의 완전한 계약 단위로 제거합니다. 지상 이동,
작성된 중력, 낙하·착지 관찰, 전투, 장착, 리스폰은 유지합니다.

## 데이터 분류

- [ ] 계정 프로필
- [x] 영속 작성 월드 데이터 템플릿
- [x] 런타임 관측
- [ ] 추론 상태
- [x] Unity 표현
- [x] 전송 / 권한 / 인프라

## 결정과 경계

- Unity는 더 이상 스페이스바 점프 입력을 만들거나 점프 엣지를 대기시키거나
  점프 임펄스를 적용하거나 Authority 점프 승인·표현을 요청하지 않습니다.
- 런타임 플레이어 의도 전송은 지상 이동만 전달합니다.
- 개발 콘텐츠 패키지는 `jump_avatar`와 `JumpPlayerFromIntent`를 발행하지
  않으며 PlayerProfile도 `jump_action`, `jump_height`, `Jump` 능력을
  작성하지 않습니다.
- `gravity_acceleration`은 지면 접지와 낙하를 소유하므로 유지합니다. 중력은
  점프를 만들지 않습니다.
- 향후 작성용 범용 애니메이션 카탈로그에는 사용하지 않는 점프 클립이 남을 수
  있지만, 플레이어 런타임에는 `JumpStart`를 선택하는 경로가 없습니다.
- 이번 변경은 기존 영속 월드를 파괴적으로 다시 쓰지 않습니다. 예전 점프 Fact는
  별도 승인을 받은 데이터 마이그레이션 전까지 보일 수 있지만 Unity와 서버
  런타임은 이를 소비하지 않습니다.

## 온톨로지 표현

현재 활성 플레이어 이동 계약:

`locomotion_action -> MovePlayerFromIntent -> AuthorityKinematic -> Unity 지상 이동`

제거된 경로:

`jump_action -> JumpPlayerFromIntent -> 점프 임펄스 / JumpStart`

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 지상 이동 | `move_avatar`와 `MovePlayerFromIntent`는 계속 발행·실행됩니다. | `DevelopmentPackagePublishesGroundLocomotionAndRetiresJump` |
| 점프 제거 | PlayerProfile, 개발 패키지, RuleDatabase에 활성 플레이어 점프 생산 계약이 없습니다. | `AvatarTriplesDeclareCompleteGroundLocomotionMeaning`; `DevelopmentPackagePublishesGroundLocomotionAndRetiresJump` |
| 중력·낙하 회귀 | 중력은 CharacterController를 계속 접지시키고 공중·착지는 관찰로 유지됩니다. | `AuthoredGravityContinuesSettlingWithoutNewMovementIntent`; `LosingGroundUsesAirborneObservationWithoutGameplayAction` |

## 성능과 멀티플레이 영향

플레이어 의도 경로에서 휘발성 boolean과 보조 Authority 미리 평가가
제거됐습니다. 프레임별 영속 쓰기는 추가하지 않았고, 멀티플레이 지상 이동은
기존 Authority 임시 lease를 계속 사용합니다.

## 갱신한 문서

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [ ] 언어팩 CSV / migration
- [x] 테스트 시나리오 목록
