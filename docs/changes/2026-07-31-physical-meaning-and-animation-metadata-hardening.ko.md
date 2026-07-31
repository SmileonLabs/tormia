# 물리 의미 및 애니메이션 메타데이터 강화

## 요약

- **날짜:** 2026-07-31
- **범위:** Unity 물리 표현, 애니메이션 Manifest와 런타임 투영
- **상태:** 구현 완료, 활성 Unity Editor 연결 뒤 MCP 검증 예정

## 의도

플레이어 착지 표현 문제를 진단하면서 발견한 허용적 표현 fallback과 코드 소유
애니메이션 생명 주기 분류를 제거합니다.

## 결정과 경계

- Unity Adapter가 엔티티를 이동하려면 Physical Meaning 이동 lease가 반드시
  필요합니다. 의미가 없으면 fail-closed로 동작합니다.
- 캐릭터 단차 높이는 이동 코디네이터 기본값이 아니라 수치형 Physical Meaning
  데이터입니다.
- 애니메이션 Manifest가 표현 생명 주기 메타데이터와 호환 별칭을 명시적으로
  소유합니다. Unity는 의도 이름이나 원본 클립 이름으로 소유권을 분류하지 않습니다.
- 쓰러짐 반응의 canonical ID는 `Anim_Collapse_Reaction`입니다.
  `Anim_Fall`은 명시적 legacy 별칭을 통해서만 이전 데이터 읽기를 지원합니다.
- Physical Meaning을 제거하면 이동 드라이버와 파생 단차 튜닝이 제거됩니다.
  생명 주기 메타데이터를 제거하거나 바꾸면 해당 라우팅도 제거되며 숨은 정규화가
  다시 만들지 않습니다.

## 검증

| 경우 | 기대 증거 |
| --- | --- |
| 완전한 Physical Meaning | 이동 lease가 설정된 드라이버만 허용하고 작성된 단차 높이를 공급합니다. |
| Physical Meaning 제거 | 이동 lease가 fail-closed로 거절되고 단차 튜닝이 0으로 초기화됩니다. |
| Manifest 소유 점프 생명 주기 | 이동 상태 소유 항목만 해석기로 전달되고 Root Motion을 명시적으로 끕니다. |
| 메타데이터 제거·충돌 | 메타데이터가 없으면 전달하지 않고 같은 의도의 소유자가 충돌하면 Manifest 검증에 실패합니다. |
| 이전 쓰러짐 ID | `Anim_Fall`은 `Anim_Collapse_Reaction`으로 해석되며 동기화된 프로필은 canonical ID만 작성합니다. |

## 갱신 문서

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
