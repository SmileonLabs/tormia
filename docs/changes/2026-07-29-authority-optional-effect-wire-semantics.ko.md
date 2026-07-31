# Authority 선택 효과 전송 의미론

## 요약

- **날짜:** 2026-07-29
- **담당:** TOV Authority 평가 경계
- **상태:** 구현 및 로컬 배포
- **관련 문제:** `equip_weapon`이 Authority까지 도달했지만 `invalid_action_effect_guard`로 거부되었습니다.

## 원인

`EquipItemOnInteractionIntent` 룰블록과 무기의 필수 트리플은 모두 존재했습니다.
하지만 Unity 직렬화가 선택 필드인 `valueFrom`과 `when`을 빈 기본 인스턴스로
발행했습니다. 서버가 비교 방식 `None`인 빈 가드를 설정된 가드로 해석하여
정상 룰블록을 거부했습니다.

## 결정

선택적 수치 소스·가드는 의미 필드가 하나라도 빈 기본값과 다를 때만 설정된
것으로 봅니다. 완전히 빈 오브젝트는 전송 형식 노이즈로 보고 미설정으로
처리합니다. 일부만 설정된 오브젝트는 계속 엄격한 검증에 실패합니다.

이는 공통 효과 의미론이며 무기·predicate·룰블록·프리팹·메시·오브젝트명
예외가 없습니다. 기존 불변 카탈로그 payload와 checksum도 다시 쓰지 않습니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 빈 Unity 기본값 | 호출된 장착 룰블록이 `item equipped_by actor`를 생성 | `EquipWeapon_IgnoresUnitySerializedEmptyOptionalEffectObjects` |
| 설정된 수치 메타데이터 | 기존 엄격한 수치 검증과 순차 가드 평가 유지 | Authority 평가기 수치 테스트 |
| 룰블록 없음 | Unity fallback 없이 장착 거부 | `EquipWeapon_WithoutRuleBlockIsRejected` |

Authority 서버 테스트 31/31을 통과했고 수정 평가기로 로컬 Docker 서비스를
재빌드·재시작했습니다.

