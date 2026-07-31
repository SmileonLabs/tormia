# Authority 장착 관계와 구역 복구

## 요약

- **날짜:** 2026-07-29
- **상태:** 구현 및 검증 완료
- **범위:** 장착 상태 정리, 구형 Zone 배정, 영속 명령 전송

## 결정

현재 장착 상태의 기준은 아이템 소유 `equipped_by` 관계입니다. 행동이 만든
`attacks_with` 관계에 대응하는 `equipped_by`가 없으면 월드 입장 과정에서
멱등 Authority 복구를 요청합니다. 복구는 잔여 기록만 철회하고 그 기록으로
아이템을 다시 장착하지 않습니다.

Zone이 없는 구형 엔티티는 저장된 X/Z 위치가 정확히 하나의 작성 Zone에
포함될 때만 배정합니다. 여러 Zone에 겹치거나 범위 밖인 엔티티는 배정하지
않습니다.

Unity의 영속 명령은 Authority 클라이언트 단위로 직렬화합니다. 서버가
`stale_revision`을 반환하면 새 명령 ID로 한 번 재시도합니다. 이는 전송
조정이며 게임 권한 규칙이 아닙니다. 장착과 공격은 계속 트리플과 연결된
Rule Block이 결정합니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 고아 장착 기록 | 잔여 `attacks_with`를 감지하고 철회 | `OrphanedAttackToolRelationRequiresEquipmentRepair` |
| 유효 장착 | 대응 `equipped_by`가 있으면 복구하지 않음 | `OrphanedAttackToolRelationRequiresEquipmentRepair` |
| 단일 Zone | 구형 엔티티가 Authority Zone 이관 대상이 됨 | `LegacyUnzonedEntityIsMigratedOnlyForOneMatchingZone` |
| 중첩 Zone | 추측으로 배정하지 않음 | `LegacyUnzonedEntityIsMigratedOnlyForOneMatchingZone` |

