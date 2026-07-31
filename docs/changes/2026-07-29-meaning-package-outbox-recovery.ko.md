# 의미 패키지 보낼 편지함 복구

## 요약

- **날짜:** 2026-07-29
- **담당:** TOV 개발
- **상태:** 구현 및 검증 완료
- **관련 현상:** 의미 패키지 편집 후 월드 입장이 `Recovering`에서 중단됨

## 의도

일시적인 전송 실패가 있어도 리비전 기반 멱등 의미 패키지 명령을 보존하되,
과거 JSON 표현 오류 하나가 이후의 모든 월드 입장을 막지 않게 합니다.

## 데이터 분류

- 영속 월드 데이터: 변경 없음. 계속 Authority가 소유합니다.
- 전송/인프라: nullable 명령 JSON과 재생 절차를 보정합니다.
- Unity 표현: 게임플레이 또는 시각 fallback을 추가하지 않습니다.

## 결정과 경계

canonical 의미 패키지 Fact에서 값이 없는 `objectEntityId`는 JSON `null`로
직렬화합니다. Unity는 기존 대기 명령도 재생 직전에 같은 형식으로
정규화하며 command ID는 유지합니다. Authority는 잘못된 payload JSON을
처리되지 않은 HTTP 500으로 내보내지 않고 정상적인
`invalid_meaning_package_payload` 거절 응답으로 바꿉니다.

보낼 편지함은 여전히 명령 성공을 추정하지 않습니다. Authority의 완료된
승인 또는 거절을 받은 명령만 제거하고, 실제로 응답이 손실된 명령은 큐에
남깁니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 신규 canonical 의미 패키지 | 선택적 엔티티 GUID가 JSON `null` | `MeaningPackageUsesJsonNullForOptionalEntityGuid` |
| 기존 대기 의미 패키지 | command ID를 바꾸지 않고 빈 선택적 GUID 정규화 | `LegacyMeaningPackageOutboxNormalizesEmptyOptionalGuid` |
| 의미 패키지 제거 | 빈 application 식별자를 유효한 zero GUID로 인코딩 | `MeaningPackageRemovalUsesAValidEmptyApplicationGuid` |
| 대기 패키지 2개가 있는 실제 입장 | 두 명령 완료, 큐 0, 세션 `InWorld` | Unity 실시간 재현, 리비전 334 |
| 잘못된 서버 payload | 예외 대신 완료된 payload 거절 반환 | Authority 역직렬화 보호 및 서버 테스트 |

## 성능과 멀티플레이 영향

정규화는 복구할 의미 패키지 명령마다 한 번 실행되는 제한된 문자열
보정입니다. 프레임 단위 작업, 추가 DB 쓰기 또는 새로운 멀티플레이 소유권
경로를 만들지 않습니다.

## 갱신 문서

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
