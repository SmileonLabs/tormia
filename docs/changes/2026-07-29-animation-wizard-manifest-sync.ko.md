# Manifest 우선 애니메이션 위자드 동기화

## 요약

- **날짜:** 2026-07-29
- **담당:** Unity 온톨로지 애니메이션 콘텐츠 파이프라인
- **상태:** 구현
- **관찰 문제:** 캐릭터 위자드가 `AnimationDatabase.asset`을 직접 수정하고,
  선택적으로 한 ActorProfile만 수정하며, 모든 신규 클립에 관련 없는 의미
  속성을 강제로 넣고 있었습니다.

## 원인

기존 편의 기능은 애니메이션 콘텐츠 Manifest보다 먼저 만들어져 저작 원본이
두 개가 되었습니다. 이후 Manifest 동기화가 위자드로 추가한 Database 항목을
지울 수 있었고, Manifest에서 제거한 항목이 Database 직접 편집을 통해 다시
생길 수 있었습니다.

## 결정

위자드는 이제 `Manifest 등록 -> 검증 -> Database/Profile 자동 동기화` 순서로
동작합니다. canonical Manifest 항목을 만들고 전체 후보 Manifest를 검증한
뒤에만 런타임 정의와 Profile 레퍼토리 소속을 생성합니다. 동기화가 실패하면
이전 Manifest를 복원합니다. Database 단독 등록과 애니메이션 의미 속성
하드코딩은 제거했습니다.

현재 애니메이션 자산과 World Authority 행동 의도도 같은 Manifest 투영을
기준으로 검사합니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 유효한 위자드 항목 | Manifest 항목이 일치하는 Database 정의와 Profile 소속을 생성 | `WizardManifestRegistrationSynchronizesDatabaseAndProfile` |
| Manifest 항목 제거 | 생성된 Database 정의와 Profile 소속도 함께 사라짐 | `WizardManifestRegistrationSynchronizesDatabaseAndProfile` |
| 현재 프로젝트 콘텐츠 | Manifest·Database·Profile·검 애니메이션·Authority 의도가 일치 | `CurrentAnimationAssetsMatchManifestProjection` |
