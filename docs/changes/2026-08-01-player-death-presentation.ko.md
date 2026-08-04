# Authority 투영 기반 플레이어 사망 표현

- 날짜: 2026-08-01
- 상태: 구현 및 애니메이션 생산 라인 검증 완료

플레이어 사망 애니메이션은 Authority가 평가한 생명력 결과를
`current_health=0` 또는 `is_alive=false`로 투영한 뒤에만 선택된다. 월드
아바타는 `death_animation_intent=Death`를 작성하고, 프로젝트 소유
`Standing Death Left 01.fbx`는 Root Motion 비활성 상태로 Manifest,
Database, PlayerProfile 레퍼토리에 `Anim_Player_Death_Left`로 등록된다.

기존 아바타는 플레이어 의미 계약 버전 11을 통해 이관되며, 이관은
누락된 사망 표현 트리플만 추가한다. 해당 트리플이나 검증된
Manifest/Profile 항목을 제거하면 표현 경로도 제거된다. Unity는 충돌,
오브젝트 이름 또는 로컬 체력 규칙으로 사망을 추론하지 않는다.

가져온 사망 클립은 Manifest의 Root Motion 비활성 상태를 유지하면서 수직과
XZ 루트 이동을 포즈에 굽힌다. CharacterController가 유일한 위치 소유자로
남기 때문에 영속 사망 자세가 공중으로 이동하지 않는다.
