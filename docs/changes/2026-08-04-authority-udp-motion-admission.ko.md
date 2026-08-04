# Authority UDP 이동 입장

## 결정

HTTP와 인증 UDP 이동을 하나의 Rule 평가 Authority 입장과 하나의 원자적 단일
Writer intent 레지스트리로 연결한다.

## 무결성과 성능

- 전송 결합, 런타임 세션, 월드 revision, Writer 세대를 등록 시점에 원자적으로
  검증한다.
- Rule 변경은 커밋 전 pending fence와 커밋 후 정확한 revision을 사용한다.
- 장애 복구와 계약 빌드는 단일 실행이며 용량을 제한한다.
- 캐시 계약은 revision 범위의 평가 결과이며 대체 규칙 엔진이 아니다.

## 증거

- Rule 제거, revision 무효화, 오래된 세션·세대, HTTP/UDP 중복 sequence,
  Writer 승계, pending 커밋, 캐시 압력 테스트를 추가했다.
- 2026-08-04 World Authority 전체 테스트 235개 통과, 실패 0개.
