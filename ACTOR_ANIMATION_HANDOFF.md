# Tormia Actor Animation 인수인계

## 현재 상태

- 브랜치: `main`
- 최신 기능 커밋: `4a33504 feat: add unified actor animation setup wizard`
- Actor Profile 온톨로지 동기화 커밋: `67e2f03 feat: sync actor profiles into ontology facts`
- 최신 Actor Profile: `Assets/Data/Ontology/Actors/PlayerProfile.asset`
- 원격 저장소: `origin/main`

## Unity에서 시작하기

1. Unity 6 프로젝트를 열고 `Assets/ithappy/Creative_Characters_FREE/Scenes/Demonstration.unity`를 연다.
2. 메뉴에서 `Tools → Ontology → Actor Animation Setup Wizard`를 연다.
3. `Actor Object`에 `OntologyPlayer` 루트 오브젝트를 지정한다.
4. `Actor Type = Player`, `Rig Type = Humanoid`, `Actor Id = Player`로 둔다.
5. `Animation Database`는 `Assets/Data/Ontology/AnimationDatabase.asset`을 지정한다.
6. `Apply Setup`을 누른다.
7. `Inject Ontology Data`를 누르면 Actor 타입/리그/능력이 월드 사실로 주입된다.
8. `Preview Intent`에 `Idle`, `Locomotion`, `Attack`, `DeathReaction`을 입력해 후보 애니메이션을 확인한다.

## Wizard가 자동으로 처리하는 것

- Actor Profile 생성 또는 갱신
- `OntologyAnimationAdapter` 추가 및 연결
- 자식 Animator 자동 검색
- `OntologyActorProfileFactSynchronizer` 추가 및 연결
- World Bootstrap, Animation Database, Actor ID 설정
- `actor_type`, `rig_type`, `animation_capability` 사실 주입

## NPC/몬스터 추가

- NPC: `Actor Type = NPC`, 적절한 NPC 오브젝트, `Rig Type = Humanoid`
- 몬스터: `Actor Type = Monster`, 몬스터 오브젝트, `Rig Type = Quadruped` 또는 `Flying`
- Wizard의 `Profile`을 비워 두면 Actor ID 기반 새 프로필을 자동 생성한다.
- 애니메이션 정의의 `actorTypes`, `rigTypes`, `requiredCapabilities`가 후보 필터로 사용된다.

## 데이터 흐름

```text
World Fact → Ontology Rule → animation_intent → AnimationDatabase → Adapter → Animator
```

## 현재 알려진 경고

- `CustomButton.isHeld` CS0414: 외부 UI 에셋의 미사용 필드 경고
- `MaterialLocation.External is obsolete`: 일부 외부 FBX Import 설정 경고
- `MCP-FOR-UNITY StdioBridgeHost started`: 정상적인 MCP 시작 정보

## 작업 시 주의

- `ProjectSettings/EntitiesClientSettings.asset`, `ProjectSettings/ShaderGraphSettings.asset`, `opencode.json`은 로컬 환경 파일로 현재 커밋 대상이 아니다.
- `OntologyDebugCanvas.prefab`은 Unity UI 설정을 실행하면 큰 직렬화 diff가 발생할 수 있으므로, 의도적으로 UI 변경을 포함할 때만 별도 검토 후 커밋한다.
- 다른 PC에서는 먼저 `git clone` 또는 `git pull`, Unity 6 프로젝트 열기, 패키지 임포트 완료를 기다린 후 작업한다.
