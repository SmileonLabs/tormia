# 런타임 관측 Fact 소유권 리팩토링

## 요약

- **날짜:** 2026-07-20
- **담당:** Tormia 개발
- **상태:** 구현 및 검증 완료
- **관련 요청:** 하드코딩·중복 코드·구조 리팩토링

## 목적

환경, 장비, 물리 콘텐츠가 늘어나도 월드 관측을 안전하게 재사용한다.
런타임 센서는 자기 관측이 바뀌었다는 이유만으로 사용자가 작성한 Fact나
다른 어댑터가 발행한 Fact를 제거하면 안 된다.

## 데이터 분류

- [x] 런타임 관측
- [x] Unity 표현

## 결정과 경계

`OntologyRuntimeObservationFacts`가 물 점유, 배우 물 감지, 지지면 접촉
센서가 공통으로 쓰는 발행 소유권을 관리한다. 각 센서는 **자기가 실제로
발행에 성공한 Fact만** 회수한다.

`OntologyWorldBootstrap.WorldRebuilt`는 `WorldChanged`와 분리했다.
`WorldRebuilt`는 리셋·복원으로 런타임 월드 인스턴스 자체가 교체된 경우만
의미한다. `WorldChanged`는 기존처럼 모든 월드/추론 갱신을 알린다. 관측
어댑터는 새 월드에 관측 Fact를 다시 발행하기 위해 `WorldRebuilt`를 쓴다.
표현 어댑터는 `WorldChanged`로 시각 상태를 갱신할 수 있다.

이 변경은 새 규칙, 프로필, 오브젝트 이름 예외를 만들지 않는다. Unity가
수영·부력·장착 허용 규칙을 소유하게 하지도 않는다.

## 온톨로지 표현

- 관측 관계: `occupies`, `immersion_depth`, `supported_by`
- 추론/표현 결과는 기존 규칙과 어댑터가 계속 소유한다.
- 수영 어댑터의 `Swimming`, `Moving`, `Idle`, `Shallow`, `Deep`은 중복
  문자열 대신 canonical 상수를 사용한다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 관측 추가/제거 | 센서는 자신이 만든 관계만 발행·회수한다. | `OntologyWaterOccupancySensorTests` PlayMode 3/3 통과 |
| 기존 작성 Fact | 센서는 기존 Fact의 소유권을 빼앗거나 제거하지 않는다. | `OntologyWorldStateTests` EditMode 4/4 통과 |
| 물리/장착 회귀 | 부력, 장착, 지지면 테스트가 계속 통과한다. | PlayMode 4/4 통과 |

## 성능과 멀티플레이 영향

- 공통 동기화기는 센서별 재사용 제거 버퍼를 사용하므로 일반 관측 갱신 때
  새 컬렉션을 만들지 않는다.
- DB 쓰기, 네트워크 트래픽, authority 명령, Zone fan-out은 추가하지 않는다.
  해당 Fact는 계속 로컬의 일시 런타임 관측이다.

## 갱신한 문서

- [x] 변경 기록 (영문·한글)
- [x] 하네스 시나리오 목록
- [ ] `PROJECT_CONTEXT.md` / `PROJECT_CONTEXT.ko.md` (제품 정책 변경 없음)
- [ ] 언어팩 CSV / migration (canonical ID 변경 없음)
