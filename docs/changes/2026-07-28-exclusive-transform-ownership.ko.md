# Transform 단일 소유권과 안정적인 배치

## 요약

- **날짜:** 2026-07-28
- **상태:** 구현 완료 및 연결된 Unity Editor 검증 완료
- **요청:** 오브젝트 이름 예외 처리 없이 첫 이동 시 플레이어가 내려갔다
  올라오는 현상과 배치 물체의 반복 튕김을 제거합니다.

## 의도

모든 Unity Transform에 하나의 표현 소유자를 부여하고, 온톨로지로 선택된 최종
충돌 형상을 기준으로 배치하며, 안정된 물리 위치만 기존 Authority 명령 경계를
통해 드물게 저장합니다.

## 데이터 분류

- 영속 월드 데이터: 승인된 `place_entity`, 저빈도 `move_entity` 명령
- 런타임 관찰: Rigidbody 속도·Sleep 상태와 지지면 접촉
- Unity 표현: Transform 소유권, 충돌 안정화, 아바타 접지

## 결정과 경계

`월드 편집 > 장착 > 로컬 컨트롤러/Actor 런타임 > RuntimePhysics >
DurableProjection`을 단일 표현 우선순위로 사용합니다. Dynamic 오브젝트는
Authority 위치로 한 번 초기화하고, 이후 활성 물리를 투영 갱신으로 덮어쓰지
않습니다. 프레임별 위치 이벤트를 만들지 않습니다. 카탈로그는 물리 프로필을
명시해야 하며 누락된 데이터에 `HeavySinking`을 자동 적용하지 않습니다.

## 온톨로지 표현

소유권은 `physical_profile`, 장착 관계, 활성 표현 어댑터에서 계산합니다.
`AuthorityKinematic`은 Unity Rigidbody를 kinematic으로 유지해야 하는 Actor의
canonical mobility 값입니다. 프리팹이나 GameObject 이름 조건은 없습니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 활성 | Dynamic 투영은 한 번만 초기화하고 Rigidbody에 소유권을 넘기며 최종 Collider로 지면 정렬 | `OntologyPlayerInputPriorityTests`, `OntologyRuntimeDataAssetTests` |
| 비활성/제거 | Dynamic 또는 장착 소유권 제거 시 영속 투영으로 복귀하고 프로필 누락 시 물리를 만들지 않음 | 동일 테스트 |
| 입장 | 캡슐 안전 접지가 영속 데이터 기록 없이 수직 속도만 초기화 | `EntryGrounding_ClearsOnlyEphemeralVerticalVelocity` |
| 첫 이동 | 입장 접지 직후 첫 `CharacterController.Move`가 계산된 지지면 위를 유지 | `EntryGroundingKeepsFirstMoveOnSupportSurface` |

2026-07-29 연결된 에디터에서 대상 EditMode 테스트 48개와 PlayMode 테스트
18개가 통과했습니다. PlayMode 검증에는 장착 소유권, 물리 프로필 동기화, 첫
이동 접지, 분리 씬 조합이 포함됩니다. Unity 물리 소유권이 일시 정지된
kinematic Rigidbody에는 더 이상 선형·각속도를 쓰지 않습니다.

## 성능·멀티플레이 영향

물리는 로컬 표현으로 유지합니다. 의미 있는 변화 후 안정된 물체만 cooldown을
거쳐 revisioned·idempotent 이동 명령을 한 번 보낼 수 있습니다. 프레임별 DB
또는 네트워크 기록은 추가하지 않습니다.

## 갱신 문서

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
