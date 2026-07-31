# 작성된 서버 충돌 프록시

## 요약

- **날짜:** 2026-07-31
- **담당:** TOV 개발 하네스
- **상태:** 구현 및 검증 완료
- **관련 요청:** 신뢰 가능한 경량 충돌 지오메트리로 서버 권한 이동 단계 시작

## 의도

World Authority가 Unity 시각 자산이 아닌 온톨로지 데이터에서 재사용 가능한
충돌 표현을 얻도록 합니다. 정상 동작하는 로컬 CharacterController를 성급하게
대체하지 않으면서 서버 고정 Tick 이동 검증의 선행 조건을 만듭니다.

## 데이터 분류

- [ ] 계정 프로필
- [x] 영속 작성 월드 데이터
- [ ] 런타임 관측
- [ ] 추론 상태
- [x] Unity 표현
- [x] 전송 / 권한 / 인프라

## 결정과 경계

영속 엔티티 Transform과 명시적 충돌 프록시 Fact가 원본입니다. Unity 작성
도구는 기존 revision 명령 경로로 이를 전송합니다. 런타임 클라이언트는 Mesh에서
계산한 권한 지오메트리를 업로드하지 않습니다. World Authority는 Capsule과 Box
프록시를 투영하지만 아직 플레이어 이동을 적분하지 않습니다.

## 온톨로지 표현

- `collision_role`
- `collision_proxy_shape`
- `collision_radius`, `collision_height`
- `collision_size_x`, `collision_size_y`, `collision_size_z`
- `collision_center_offset_x`, `collision_center_offset_y`,
  `collision_center_offset_z`

기존 이동 액션과 `MovePlayerFromIntent` Rule Block이 계속 이동 행동을
허용합니다. `LocalCharacterController`는 현재 표현 Physical Meaning을
유지하며 충돌 프록시 Fact는 서버 지오메트리를 추가할 뿐 두 번째 Unity 이동
소유자를 만들지 않습니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 활성화 / 추가 | 완전한 플레이어 Capsule과 일반 Box 데이터가 제한된 서버 프록시 생성 | `PlayerMotionPolicyTests.AuthoredCapsuleCollisionProxyBuildsWithoutVisualFallback`; `AuthoredBoxProxySupportsReusableUgcCollisionMeaning` |
| 비활성화 / 제거 | 필수 Capsule 치수를 제거하면 생성 거절 | `PlayerMotionPolicyTests.RemovingRequiredCollisionProxyDimensionFailsClosed` |
| 회귀 / 예외 | Zone 검증이 중심점이 아닌 전체 프록시 범위를 계산 | `PlayerMotionPolicyTests.ZoneValidationUsesWholeProxyInsteadOfOnlyItsCenter` |

## 성능과 멀티플레이 영향

프록시 구성은 영속 Fact에서 조회하며 기본 숫자만 포함합니다. 렌더 Mesh나 프레임
단위 영속 쓰기를 추가하지 않습니다. 고정 Tick 적분과 스냅샷 보정은 이번 단계
범위가 아닙니다.

## 갱신한 문서

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] 필요 시 언어팩 CSV / migration
- [x] 필요 시 테스트 시나리오 목록
