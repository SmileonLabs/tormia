# 월드 입장 릴리스·마이그레이션 준비 상태

## 요약

- **날짜:** 2026-08-02
- **담당:** TOV 개발 하네스
- **상태:** 구현 및 검증 완료

## 결정

월드 입장은 아바타나 월드를 준비하기 전에 이미 게시된 불변 콘텐츠
릴리스를 검증한다. 입장 과정에서는 규칙·액션 정의를 게시하거나 콘텐츠
패키지를 활성화하지 않는다. 개발 하네스가 설정된 규칙·액션 소스 ID를
fingerprint하여 payload 변경을 월드 입장이 아니라 플레이 전에 찾는다.

DB 프로세스 Health는 스키마 준비 상태가 아니다. `-RequireServices`를 사용하면
하네스가 저장소의 모든 순서형 마이그레이션을
`platform_schema_migrations`와 대조한다.

기존 영속 볼륨의 ledger가 비어 있어도 모든 마이그레이션이 미적용됐다고
가정하지 않는다. 별도 명시적 adoption 도구가 스키마·인덱스·제약 조건과
migration 016의 소유권 백필을 확인한 뒤에만 001~016을 기록할 수 있다.
부분 ledger와 호환되지 않는 fingerprint는 거부한다. migration 017은 데이터
복구 작업이므로 스키마 모양만으로 적용됐다고 판단하지 않고 정상
마이그레이션으로 남긴다.

## 검증

- `scripts/verify-development.ps1 -SkipServerBuild`: 통과
- `scripts/verify-development.ps1 -SkipServerBuild -RequireServices`: 통과
- 현재 로컬 DB: 17개 마이그레이션 기록 확인
- 기존 스키마 fingerprint dry run: 스키마·소유권 백필 호환, 데이터 쓰기 없음

처음에 ledger가 비어 있다고 보였던 결과는 준비 상태 검증기의 셸 인용 오류가
원인이었다. 설정된 DB와 사용자를 사용하는 직접 `psql` 호출로 잘못된 결과를
수정했다. 라이브 DB는 변경하지 않았다.
