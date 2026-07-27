# 월드 콘텐츠 활성화와 서버 권한 액션 기반

- **날짜:** 2026-07-22
- **상태:** 구현 및 하네스 검증 완료.

## 결정과 경계

각 월드는 활성화할 콘텐츠 패키지/버전을 명시적으로 선택합니다. 해당 패키지의 멤버만 선택을 바꿀 수 있고, 활성화하는 버전에는 발행된 정의가 하나 이상 있어야 합니다.

발행된 `action_effect` 정의는 불변입니다. 초기 서버 실행 계약은 의도적으로 단일 엔터티-대-엔터티 Fact만 허용합니다. 플레이어는 등록된 아바타, 대상, 선택 도구, 패키지와 정의 식별자만 보냅니다. Authority가 활성화된 불변 payload를 해석하여 `source_type = action` Fact를 기록합니다. 이 변경은 월드 revision을 전진시키며 일반 command ID로 멱등 처리됩니다.

Authority는 조건, 구조화된 효과, 임의 subject/object 패턴, 클라이언트가 보낸 predicate를 거부합니다. 이 기능들은 headless evaluator가 소유하는 다음 단계로 남겨 두어 Unity가 게임 규칙의 숨은 소유자가 되지 않도록 합니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 패키지 | 멤버는 발행 콘텐츠가 있는 패키지/버전을 활성화할 수 있다 | `set_content_package`, `007_world_content_packages.sql` |
| 액션 | 등록된 아바타는 활성화된 발행 단일 트리플 액션만 실행할 수 있다 | `WorldAuthorityRepository`의 `execute_action` |
| 비활성/거부 | 비활성·불일치 정의와 임의 클라이언트 predicate는 거부된다 | 동일 Authority 검증 |
| 빌드 | Authority와 하네스 매니페스트 빌드가 성공한다 | `scripts/verify-development.ps1` |
