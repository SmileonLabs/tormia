# Authority 전투 수직 슬라이스

## 요약

- **날짜:** 2026-07-27
- **상태:** 구현 및 검증 완료
- **범위:** World Authority, 온톨로지 액션 정의, Unity 표현,
  무기·몬스터·애니메이션·VFX 외부 에셋 어댑터, 하네스 증거

## 의도

Unity 코드, 프리팹명, 외부 에셋명이 피해 규칙의 주인이 되지 않도록 하면서
재사용 가능한 첫 무기·몬스터 전투 생산 라인을 구축합니다.

## 소유권 결정

- 계정 캐릭터 외형은 계속 계정 데이터가 소유합니다.
- 장착 도구, 대상 체력, 생존 상태, 월드 드롭 가능 상태는 리비전이 있는
  월드 상태입니다.
- 조준 입력, 클릭 위치, 애니메이션 재생, VFX 수명은 일시 상태입니다.
- Unity는 actor, target, tool, 불변 정의 ID만 제출합니다.
- World Authority가 조건을 평가하고 모든 영속 효과를 한 번의 멱등 트랜잭션에
  적용합니다.

## 온톨로지와 표현

- `equip_weapon`이 `equipped_item`을 설정합니다.
- `attack`은 `MeleeAttack` 능력을 제공하는 장착된 `Sword`를 요구합니다.
- 경계형 수치 조정이 `current_health`를 변경합니다.
- 범용 사후 조건이 체력 0에서 사망 및 드롭 Fact를 설정합니다.
- 프로젝트 래퍼 프리팹이 외부 시각 에셋을 참조하며, 애니메이션과 VFX는 의미
  의도로 선택합니다.

## 검증

- 기존 `social_village 1.0.0`을 덮어쓰지 않도록 전투 규칙 패키지를
  불변 신규 버전 `1.1.0`으로 발행한다.
- 새 패키지 버전에서 모든 정의가 조회되도록 `help/talk`은 정의 버전 2,
  `attack`은 4, `equip_weapon`은 2로 함께 승격한다.
- `F` 장착은 마우스가 가리킨 무기를 우선하되, 가느다란 검을 정확히
  클릭하지 않아도 주변 3m 안의 가장 가까운 무기를 선택한다.
- 플레이어 전투 FBX는 각 `With Skin` 원본에서 Avatar를 생성하는 Humanoid로
  가져온다. 무기 표현은 플레이어 루트의 비어 있는 Animator가 아니라 실제
  Avatar가 연결된 첫 번째 Humanoid 자식 Animator를 선택한다.
- 개발 전용 콘텐츠 발행은 계정별 패키지 ID를 사용한다. 서로 다른 로컬 계정이
  하나의 `social_village` 제작 패키지 소유권을 두고 충돌해 잘못된
  `403 Forbidden`을 받지 않는다.
- 외부 무기·몬스터 패키지의 Built-in Standard 머티리얼 원본은 변경하지 않는다.
  프로젝트 래퍼의 표현 어댑터가 베이스·노멀·메탈릭·이미션 맵을 옮긴 캐시형
  URP Lit 머티리얼을 만들어 프리뷰와 런타임 오브젝트가 분홍색으로 나오지 않게 한다.
  원본에서 `_EMISSION`이 활성화된 경우에만 이미션 값을 옮겨, 비활성 잔여 값이
  몬스터 알베도를 흰색으로 덮지 않게 한다.
- 자동 배치 발행은 관련 없는 시작 편의 옵션 `connectOnStart`가 아니라 실제 월드
  입장 완료와 편집 권한으로 판단한다. `F`가 기존 로컬 무기나 발행 중인 무기를
  선택하면 엔티티와 의미 Fact를 먼저 발행한 뒤 Authority 장착 액션을 재시도한다.

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 활성 | 장착과 세 번의 공격으로 30 → 20 → 10 → 0, 사망, 드롭 생성 | `scripts/run-combat-authority-smoke.ps1` |
| 비활성 | 패키지를 끄거나 대상이 사망하면 추가 공격 거절 | Authority 스모크 및 평가기 테스트 |
| 재전송 | 마지막 command ID를 다시 보내도 피해가 중복되지 않음 | Authority 스모크 |
| 복원 | 새 로그인 뒤에도 체력 0과 드롭 가능 상태 조회 | Authority 스모크 |
| Unity 자산 | 검 3종, Beholder, 애니메이션 의도, VFX 연결 | `OntologyCombatVerticalSliceAssetTests` |
| Humanoid 매핑 | 모든 플레이어 전투 클립에 Humanoid 원본 Avatar 생성 | `PlayerCombatClipsHaveHumanoidSourceMappings` |
| 패키지 소유권 | 서로 다른 개발 계정은 서로 다른 불변 패키지 ID 생성 | `DevelopmentPackageIdentityIsScopedToItsPublishingAccount` |
| 머티리얼 파이프라인 | 외부 Standard 머티리얼의 베이스 텍스처가 URP 표현 브리지에서 유지됨 | `ImportedCombatMaterialsBridgeFromStandardToUrpLit` |
| 배치 발행 | 월드에 입장한 편집자는 시작 연결 설정과 무관하게 발행 가능 | `PlacementPublicationUsesRuntimeStateAndWorldPermission` |
| 씬 구성 | Bootstrap에서 전투 입력과 풀링 VFX 어댑터 로드 | `TormiaSceneCompositionSmokeTests` |

## 성능

VFX는 작은 재사용 풀을 사용합니다. 포인터 대상 선택과 애니메이션 상태는
로컬에서 처리합니다. 명시적인 장착·공격 명령만 영속 리비전을 만들며 프레임별
전투 관측을 PostgreSQL에 기록하지 않습니다.
