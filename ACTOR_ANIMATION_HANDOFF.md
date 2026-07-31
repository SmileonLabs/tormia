# TOV Actor Animation Handoff / 배우 애니메이션 인수인계

## Current production path / 현재 생산 경로

1. Open `Tools → Ontology → Animation Content → Production Line`.
2. `Create / Migrate` seeds the manifest only when it is empty.
3. Edit `Assets/Data/Ontology/AnimationContentManifest.asset`.
4. Assign a canonical animation ID, intent, clip, actor/rig compatibility,
   actor profile, playback policy, provenance and content version.
5. Run `Validate + Synchronize`.
6. The tool generates `AnimationDatabase.asset` and each actor profile's
   animation repertoire. Runtime adapters resolve those projections.

1. `Tools → Ontology → Animation Content → Production Line`을 엽니다.
2. `Create / Migrate`는 매니페스트가 비어 있을 때만 최초 데이터를 만듭니다.
3. `Assets/Data/Ontology/AnimationContentManifest.asset`을 편집합니다.
4. canonical 애니메이션 ID·의도·클립·배우/리그 호환성·배우 프로필·재생
   정책·출처·콘텐츠 버전을 지정합니다.
5. `Validate + Synchronize`를 실행합니다.
6. 도구가 `AnimationDatabase.asset`과 각 배우 프로필의 애니메이션
   레퍼토리를 생성하고 런타임 어댑터가 그 투영을 해석합니다.

## Runtime ownership / 런타임 소유권

- Authority action definitions own accepted transient gameplay action intent.
- Authority equipment relations own persistent equipped state.
- Unity ephemeral observations own idle, locomotion, jump, fall and landing
  presentation state. They never become durable Facts.
- Actor profiles explicitly grant animation repertoire membership.
- The manifest owns clips, layers, masks, looping, root motion and transitions.
- Gameplay capabilities are not animation metadata gates.

- Authority 행동 정의가 승인된 일시 게임플레이 행동 의도를 소유합니다.
- Authority 장착 관계가 지속 장착 상태를 소유합니다.
- Unity 일시 관찰이 Idle·이동·점프·낙하·착지 표현을 소유하며 영속 Fact로
  저장하지 않습니다.
- 배우 프로필이 애니메이션 레퍼토리를 명시적으로 허용합니다.
- 매니페스트가 클립·레이어·마스크·반복·루트 모션·전환을 소유합니다.
- 게임플레이 capability를 애니메이션 메타데이터 필터로 사용하지 않습니다.

## UGC review / UGC 검토

Open `Tools → Ontology → Animation Content → UGC Review`. Development-time
submissions accept FBX files only, require attribution and license metadata,
and enter `Assets/UGC/Staging/Animations`. Validation must pass before explicit
approval moves content to `Assets/UGC/Approved/Animations` and registers it in
the manifest.

`Tools → Ontology → Animation Content → UGC Review`를 엽니다. 개발 단계
제출은 FBX만 허용하고 출처·라이선스 메타데이터가 필요하며
`Assets/UGC/Staging/Animations`에 격리됩니다. 검증을 통과하고 명시적으로
승인해야 `Assets/UGC/Approved/Animations`로 이동하여 매니페스트에
등록됩니다.

This editor workflow is the trusted internal foundation. Shipping arbitrary
user uploads at runtime still requires a separate server-side virus scan,
license policy, rig/clip conversion build worker, immutable bundle publishing,
and signed delivery catalog.

이 에디터 흐름은 신뢰 가능한 내부 기반입니다. 임의 사용자 파일을 런타임에
배포하려면 별도 서버의 악성 파일 검사, 라이선스 정책, 리그·클립 변환 빌드
워커, 불변 번들 발행, 서명된 배포 카탈로그가 추가로 필요합니다.

## Verification / 검증

- Run `scripts/verify-development.ps1`.
- Run `OntologyAnimationProductionLineTests` in Unity EditMode.
- Confirm no new Unity Console errors.
- When Authority services are expected, run development verification with
  `-RequireServices` and the combat Authority smoke.
