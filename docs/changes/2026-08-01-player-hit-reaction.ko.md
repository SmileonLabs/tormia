# Authority 투영 기반 플레이어 피격 반응

- 날짜: 2026-08-01
- 상태: 구현 및 애니메이션 생산 라인 검증 완료

플레이어 피격 표현은 `current_health`를 감소시킨 더 새로운 Authority
투영을 소비한다. PlayerProfile이 `HitReaction` 의도를 작성하고,
프로젝트 소유 Mixamo FBX를 Root Motion 비활성 상태로 검증된
Manifest, Database, Profile 생산 라인에 등록했다.

피격 의도 트리플이나 Manifest/Profile 소속을 제거하면 표현 경로도
제거된다. Unity 충돌이나 오브젝트 이름은 피해나 피격 반응 권한을
만들지 않는다.

기존 월드 플레이어는 의미 계약 버전 10을 통해 이관된다. 이관은
누락된 `hit_animation_intent=HitReaction` 트리플만 작성한 뒤 버전 표식을
올린다. 다른 플레이어 의미를 복구하지 않으며, 이관 이후 사용자가
해당 트리플을 제거하면 피격 표현도 제거된 상태로 유지된다.

실시간 revision 알림은 확정된 피해 발생 ID, 대상, Rule 평가가 피해를
만들었는지를 함께 전달한다. 대상 표현기는 접촉 시 해당 ID를 한 번만
소비한다. 체력 투영 비교는 전송 장애 시의 대체 경로로만 남으며 이미
소비한 발생 건을 다시 재생하지 않는다.
