# 온톨로지 런타임 성능 증거

## 요약

- **날짜:** 2026-08-02
- **담당:** TOV 멀티 에이전트 성능 감사
- **상태:** 구현 및 부분 검증
- **요청:** 증분 평가나 런타임 계약 캐시를 추가하기 전에 실제 온톨로지 런타임 증폭 경로를 측정한다.

## 의도와 소유권

이 변경은 런타임 관찰 지표만 추가한다. 작성 Triple, 배정 Rule Block, 추론
결과, 영속 월드 상태, Unity 표현 소유권은 바뀌지 않는다. Projection 인덱스는
Authority가 이미 승인한 행만 보관하며 빠진 권한을 추론할 수 없다.

## 결정

- 규칙 평가, Headless 입력·컴파일 작업, Projection 적용 작업을 측정한다.
- reduced Zone을 매초 평가하지 않고 기존 active/reduced 작성 주기를 지킨다.
- 수신 Projection을 엔티티·subject 기준으로 한 번 인덱싱해 반복 전체 배열 순회를 제거한다.
- revision·package 무효화 증거가 포함된 snapshot·컴파일 실측 전에는 지속 런타임 계약 캐시를 추가하지 않는다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| Core 규모 측정 | 결과를 바꾸지 않고 안정화 평가 지표 기록 | 명시적 EditMode 측정: 엔티티 1,000개, 규칙 100개, Fact 100,100개; 평가 0.426 ms; 전체 테스트 286 ms |
| Projection 교체 | 이전 인덱스 행을 제공하지 않음 | `OntologyAuthorityProjectionIndexTests` |
| reduced Zone | 매 스케줄러 루프가 아니라 5초 경계에서 실행 | `WorldZoneSimulationSchedulePolicyTests` |
| 서버 회귀 | 기존 Authority 동작 유지 | 서버 테스트 92개 통과 |

## 남은 증거 조건

지속 런타임 계약 캐시나 Headless 평가 문맥을 구현하기 전에 Action snapshot
행 수·시간, Rule/Action 역직렬화·컴파일, cache hit/miss, Projection 크기·적용
시간, revision·package 무효화를 측정한다.

## 2차 측정과 개선

- Action metric은 제한된 tag만 사용하고 원본 UGC Action ID를 제외한다.
- Fact 100,000개·동시 Action 20개의 대표 실측은 요청 범위 재사용 전
  8,495 ms, 적용 후 6,236 ms였다.
- 순수 평가 p95는 6,383 ms에서 27.998 ms로 감소했으며, 요청별 Snapshot
  컴파일 p50 약 3,498 ms가 남은 지배 비용이다.
- Headless 입력은 하나의 Repeatable Read source revision을 사용하고 오래된
  추론 결과를 폐기한다.
- 비변경 matcher overlay와 다중 인스턴스 게시 fence가 마련되기 전에는
  revision 범위 지속 캐시를 보류한다.

## 3차 측정과 안전 경계

- 정규 Intent 매칭은 비변경 요청 오버레이를 사용한다. 동시 평가기가 기본
  Snapshot을 변경하지 않고 matcher 잠금으로 직렬화되지도 않는다.
- 주 효과와 후속 Rule 효과는 하나의 명령 범위 provenance 보존 오버레이를
  공유한다. 후속 Rule은 전체 Snapshot 재로딩 없이 앞서 성공한 변이를
  순서대로 본다.
- Fact 100,000개에서 후속 Rule 0/1/4개 모두 준비 로드 1회, 후속 재로딩
  0회였다. 워밍업 후 Snapshot 컴파일은 610.753/422.798/562.736ms였고 4개
  Rule 체인은 0.210ms였다.
- Headless 발행은 원자적 revision/완료/관측 compare-and-set 펜스와 최종
  PostgreSQL revision 검사로 오래된 다중 인스턴스 결과를 거절한다.
- 전체 서버 회귀 테스트 122/122가 통과했다. 다음 성능 후보는 정확한 revision
  기반 불변 계약 캐시다. 쿨다운 획득 후 commit 실패에 대비한 예약 취소 API는
  정확성 후속 과제로 남는다.

## 4차 측정: 정확한 revision 컴파일 계약 캐시

- 캐시 키는 월드 ID, 정확한 revision, 평가기 schema version, canonical
  SHA-256 활성 콘텐츠 manifest로 구성한다. 빌드 키와 Fact 행은 하나의 전용
  Repeatable Read DB Snapshot에서 읽는다.
- 캐시 계약은 불변이다. 명령 변이는 전체 월드를 복제하지 않고 변경된 키의
  raw-row delta와 semantic addition/tombstone을 사용한다. Rule 또는 package
  제거는 새 exact key를 만들며 이전 캐시 fallback 없이 fail-closed한다.
- overflow를 포함한 같은 키 요청은 요청 취소와 분리된 하나의 빌드를 공유한다.
  resident 용량, pending key, 동시 빌드, 추정 보존 byte를 제한하며 초과 pending
  key는 backpressure로 fail-closed한다. metric에는 무제한 UGC ID를 넣지 않는다.
- Fact 100,000개에서 cold compile 464.556ms, cache-hit read 15.742ms, 첫
  committed delta mutation 4.902ms였다. 서버 회귀 테스트 136/136이 통과했다.

## 5차 경계: 서비스 수명과 retained memory

- 공유 빌드는 host stopping token과 설정 가능한 기본 30초 owner timeout을
  사용한다. 요청 취소는 waiter에만 적용되며 DB 읽기와 긴 compile/estimate
  반복문은 owner 취소를 협력적으로 확인한다.
- retained-memory 회계는 컴파일 월드 인덱스와 raw provenance container를
  포함하고 포화 연산을 사용한다. oversized 계약은 현재 요청에 한 번 반환하지만
  resident로 받아들이지 않는다.
- MeterListener 회귀 증거가 resident entry/byte, overflow pending key,
  oversized bypass, timeout 정리, active build 중 Dispose를 검증한다. 전체 서버
  회귀 테스트 149/149가 통과했다.
