# 계정 소유 플레이어 프로필 온톨로지

## 요약

- **결정:** 캐릭터가 영구적으로 소유하는 온톨로지 관계를 공유 월드 Fact와 분리해 저장합니다.
- **상태:** 구현 및 로컬 검증 완료.
- **범위:** 계정 프로필, 영구 계정 데이터, Authority API, Unity 계정 입장 연동.

## 데이터 소유권

`player_characters.profile_relations`에는 `Player → has_skill → Build`,
`Player → owns → Pet_01`, `Player → carries → Item_01`처럼 canonical English
ID를 쓰는 트리플을 저장합니다. 이것은 캐릭터 프로필 데이터이며 월드 Fact,
관측 Fact, 추론 상태, 월드 규칙의 대체물이 아닙니다.

프로필 관계는 범용 구조입니다. 나중에 펫, 인벤토리, 스킬 기능을 만들 때 각
기능의 규칙과 Unity 어댑터가 같은 관계를 해석합니다. 따라서 프리팹명이나
메시명에 의존하는 예외 코드를 추가하지 않습니다.

## Authority 계약

`PUT /v1/account/characters/{characterId}`는 command ID와 예상 프로필 리비전을
포함한 전체 프로필 스냅샷을 받습니다. Authority는 계정 소유권과 리비전을
검사하고 command ID를 기록합니다. 같은 명령을 다시 보내면 두 번 적용하지
않고 최초 리비전을 반환합니다.

Unity는 World Authority Client만 사용하며 PostgreSQL이나 Redis에 직접 연결하지
않습니다. `OntologyWorldAuthorityAccountEntryFlow`는 현재 장착된 캐릭터 파츠를
이 계약으로 저장할 수 있습니다. 화면 표현은 계속 Unity의 책임입니다.

## 검증

| 경우 | 결과 | 증거 |
| --- | --- | --- |
| 프로필 관계 추가 | 통과 | `Player → has_skill → Build` 저장, 프로필 리비전 1 → 2 증가 |
| 같은 명령 재시도 | 통과 | revision 2와 `isReplay=true` 반환, 관계 중복 없음 |
| 프로필 관계 제거 | 통과 | 새 리비전에서 관계 제거, 대시보드가 빈 목록 반환 |
| 로컬 서비스 | 통과 | migration `005_account_profile_ontology.sql` 적용 뒤 PostgreSQL·Redis·World Authority 정상 |
| Unity 컴파일 | 통과 | Unity refresh 뒤 새 Console 오류 없음 |

## 후속 작업

이번 작업은 데이터 기반입니다. 이후 캐릭터 선택, 외형 미리보기, 플레이어
온톨로지, 월드 입장 화면은 이 계약을 읽는 하이라이키 기반 UI로 구현합니다.
