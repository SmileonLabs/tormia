# 기본 플레이어 온톨로지 프로필

## 결정

최소 플레이어 온톨로지는 메시 이름이나 숨겨진 Unity fallback이 아니라
계정/프로필 자산이 선언합니다. 프로필이 기본 개념과 선택적 작성 Fact를
소유합니다. 기존 애니메이션 호환성을 위해 `animation_capability` 투영은
유지하고, 같은 값은 canonical `has_capability` 관계로도 노출합니다.

## 범위

- 계정/프로필 데이터: `OntologyActorProfile.defaultConcepts`, `defaultFacts`
- 월드 투영: 액터 입장 시 동기화기가 해당 데이터를 월드에 게시
- 씬의 Player 오브젝트에서는 기본값인 `Agent`, `PlayerControlled`,
  `Humanoid`를 제거하고 프로필이 소유합니다. `Creature`는 모든 플레이어의
  기본값이 아니므로 씬 작성 데이터로 유지합니다.
- `ontologyCapabilities`가 `Locomotion`, `Interaction` 같은 게임 능력을
  소유합니다. 기존 `capabilities` 배열은 애니메이션 설정 호환 필드로
  유지하고 `animation_capability`로만 투영합니다.
- 런타임 관측 Fact와 추론 Fact는 프로필에 저장하지 않고 분리 유지

## 검증

- `scripts/verify-development.ps1 -SkipServerBuild`: 통과
- Unity 하네스 래퍼는 설정되지 않은 PowerShell `$LASTEXITCODE`를 읽어
  테스트 실행 전에 중단됨. Unity 테스트가 통과했다고 판단하기 전에
  기존 하네스 스크립트 문제를 먼저 고쳐야 함.
