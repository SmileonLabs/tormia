# 매니페스트 기반 애니메이션 생산라인

## 요약

- **날짜:** 2026-07-28
- **담당:** TOV 개발 하네스
- **상태:** 구현 및 검증
- **관련 요청:** 기본 이동과 전투를 포함하여 트리플·프로필 메타데이터와
  업로드한 클립으로 애니메이션을 데이터 기반 추가

## 의도

클립마다 런타임 코드를 다시 연결하는 방식을 하나의 검증된 생산라인으로
교체합니다. 제작자나 개발자가 canonical 메타데이터와 클립을 제공하면 생성된
런타임 투영이 게임플레이 코드를 추가하지 않고 호환 배우를 구동합니다.

## 데이터 분류와 경계

- 영속 월드 데이터: Authority 행동 정의와 장착 관계
- 계정 프로필 데이터: 배우의 애니메이션 레퍼토리
- 일시 관찰: 이동, 접지, 수직 속도, 점프와 착지 단계
- Unity 표현: 클립, 마스크, 레이어, 반복, 전환, 루트 모션
- 빌드·배포 메타데이터: 출처, 라이선스, 체크섬, 배포 키

`AnimationContentManifest`가 저작 원본이며 `AnimationDatabase`와 프로필
애니메이션 ID 배열은 생성 투영입니다. 게임플레이 상태에 대해 Unity는 계속
World Authority와만 통신합니다.

## 온톨로지 표현

canonical 영문 의도 ID가 Authority 표현 메타데이터와 Unity 콘텐츠를
연결합니다. 메시·프리팹·FBX·Animator 상태·GameObject 이름은 게임플레이
예외가 되지 않습니다. 배우 레퍼토리는 애니메이션 표현 권한이며 게임플레이
capability는 별도로 Authority가 소유합니다.

## 활성화 및 제거 동작

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 활성화 | 호환 매니페스트 항목이 명시적 배우 레퍼토리를 통해 기본·장착·승인 행동 애니메이션을 해석한다. | `OntologyAnimationProductionLineTests` |
| 제거 | 항목 제거 시 런타임 정의와 프로필 소속이 함께 사라지고 오래된 생성 데이터가 되살리지 못한다. | 매니페스트 동기화 구현과 제거 회귀 검증 |
| 잘못된 입력 | 중복 ID, 누락 클립, 알 수 없는 행동 의도, 발행 버전 불일치는 검증에 실패한다. | `OntologyAnimationProductionLineTests` |
| UGC 격리 | 출처·라이선스가 있는 FBX가 격리되며 검증·승인된 콘텐츠만 매니페스트에 들어간다. | `OntologyAnimationUgcPipeline` |

## 성능과 멀티플레이 영향

프레임별 상태 해석은 로컬 일시 상태이며 영속 Authority 명령이나 DB 이벤트를
만들지 않습니다. 승인된 버전 행동과 영속 장착 변경만 Authority 경계를
통과합니다.

## 갱신 문서

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
