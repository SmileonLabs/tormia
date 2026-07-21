# 멀티플레이 월드 편집 권한 게이트

## 요약

- **결정:** 선택한 월드의 `owner`/`editor`/`viewer` 역할을 계정-월드 세션 계약의 일부로 취급합니다.
- **상태:** 구현 및 검증 완료.
- **범위:** 계정/세션 데이터, 영속 월드 편집, Unity 표현.

## 변경 내용

`OntologyWorldAuthorityClient`가 `CurrentWorldRole`과
`CanEditCurrentWorld`를 제공합니다. `OntologyWorldAuthorityAccountEntryFlow`도
선택된 역할을 UI와 세션 소비자에게 노출합니다. 브리지는 인증된 `owner` 또는
`editor`일 때만 영속 편집 전송을 자동으로 활성화합니다. 최종 권한은 서버가
담당하며, 서버는 권한·리비전·idempotent 명령 ID를 확인한 뒤 명령을 적용합니다.

## 검증

| 경우 | 기대 결과 | 근거 |
| --- | --- | --- |
| owner/editor 세션 | 현재 리비전으로 영속 편집 명령을 보낼 수 있음 | 클라이언트 역할 게이트와 서버 `CanEdit` 검사 |
| viewer 세션 | 자동 편집 전송을 하지 않으며 서버도 `forbidden`으로 거부 | `CanEditCurrentWorld == false`와 서버 분기 |
| 오래된 리비전 | 명령이 거부되고 현재 리비전을 반환 | 서버 `stale_revision` 분기 |
| 재전송된 명령 ID | 월드 이벤트를 중복 생성하지 않고 기존 결과 반환 | `FindCommandResult` 재생 경로 |

## 후속 작업

다음 UI 단계에서 편집 버튼 활성화와 읽기 전용 안내를
`CanEditCurrentWorld`에 연결합니다. 패널마다 권한 규칙을 복제하지 않습니다.
