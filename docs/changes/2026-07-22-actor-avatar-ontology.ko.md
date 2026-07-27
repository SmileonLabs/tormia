# Actor 및 세계 Avatar 온톨로지 투영

- **날짜:** 2026-07-22
- **상태:** 구현 및 Unity 개발 하네스 검증 완료

## 결정과 경계

계정 캐릭터 관계는 휴대 가능한 계정 소유 데이터로 유지합니다. Unity는 선택한 로컬 Avatar의 런타임 온톨로지 뷰에만 이를 투영하고, 투영을 해제하면 함께 제거합니다. 이 과정에서 작성된 월드 Fact를 만들지 않습니다.

직업, 팀, 클래스, 진행도처럼 세계에만 속하는 플레이어 의미는 별도 world-avatar profile 오버레이에 둡니다. `set_avatar_profile_relations` revisioned World Authority 명령으로 저장하며, 요청한 사용자의 등록 Avatar만 변경할 수 있습니다.

Actor별 액션 후보와 액션 실행 API는 NPC도 재사용합니다. `VillagerProfile`과 social-village 기능 팩이 첫 데이터 예시입니다. 현재 NPC 결정 컨트롤러는 로컬 개발 시뮬레이션이므로, 서버 액션 카탈로그가 발행되기 전에는 공유 월드의 권한 실행으로 취급하지 않습니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 투영 | `Self has_skill Talk`이 로컬 Avatar 런타임 Fact가 됨 | `OntologyAccountProfileRelationProjectorTests` |
| 해제 | 작성 데이터 삭제 없이 투영 Fact만 제거됨 | 같은 테스트 |
| NPC | Villager가 데이터 기반 help 액션을 받고 실행함 | `OntologyMultiActorActionTests` |
| Authority | World Authority Docker 빌드 성공 | `scripts/verify-development.ps1` |
