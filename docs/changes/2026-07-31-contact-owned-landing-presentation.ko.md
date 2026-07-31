# 접촉 소유 착지 표현

날짜: 2026-07-31

## 결정

가까운 지지면 관찰과 실제 지면 접촉은 서로 다른 일시적 신호입니다. 착지
표현은 작성된 `WalkableSupport`와 CharacterController의 접지/`Below` 충돌
결과를 모두 요구합니다. Airborne과 Fall은 별도 canonical Manifest 항목을
사용하고, Landing은 작성된 구간이 끝난 뒤 작성된 기본 의도로 블렌딩합니다.

## 온톨로지 경계

- 점프 권한은 계속 불변 점프 액션과 배정된 Rule Block이 소유합니다.
- 수직 속도와 충돌 접촉은 일시적 관찰입니다.
- 애니메이션 의도와 재생 구간은 Manifest에 작성된 표현 데이터입니다.
- Unity는 표현만 선택하며 게임플레이 권한이나 영속 이동 Fact를 만들지
  않습니다.

## 제거 경로

지지면 근접은 착지를 만들어낼 수 없습니다. Airborne, Fall 또는 Landing
Manifest 계약을 제거하면 클립 이름, 고정 타이머 또는 오브젝트 이름
fallback으로 Idle을 선택하지 않고 검증에 실패합니다.

## 기존 캐릭터 레퍼토리 동기화

ActorProfile이 표현 레퍼토리를 처음 투영한 뒤 런타임 월드가 교체될 수
있습니다. 프로필 동기화기는 이제 `WorldRebuilt`에서 `ActorProfile` 원본
기여분을 다시 투영합니다. 프로필 애니메이션을 제거하면 해당 기여분도
철회되므로 기존 캐릭터가 오래되거나 이름 기반인 fallback 없이 새 Manifest
콘텐츠를 받습니다.
