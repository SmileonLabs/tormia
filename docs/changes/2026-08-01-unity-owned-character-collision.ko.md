# Unity 소유 캐릭터 충돌 표현

## 요약

- 날짜: 2026-08-01
- 상태: 구현 완료, 검증 진행 중
- 범위: Unity 표현, Physical Meaning, 플레이어 충돌 하네스

## 결정

온톨로지 데이터는 물리 사용 권한과 설정값을 선택하고 실제 로컬 충돌,
경사, 단차 해결은 Unity Physics가 담당한다. 장애물을 샘플링해 위쪽 좌표를
직접 주입하던 자체 단차 계산기는 제거한다.

`maximum_step_height`는 canonical 작성 데이터로 유지하며 Unity
`CharacterController.stepOffset`에 매핑한다. 같은 Physical Profile이 경사
제한, 스킨 폭, 최소 이동 거리도 소유한다. 프로필을 제거하면 컨트롤러
설정과 이동 lease가 함께 제거된다.

## 경계

- Triple/Rule Block/Physical Meaning: 권한과 설정값
- Unity CharacterController: 프레임당 한 번의 충돌 해결 Move
- Support Probe: Unity Cast 기반 임시 관측만 수행
- World Authority: 작성된 경량 프록시와 고정 Tick 공유 이동 담당
- 월드 입장 1회 접지 정렬: 입력 개방 전 준비 초기화

## 검증

- 활성 경로: WalkableSupport 단차를 프로필의 stepOffset으로 Unity가 해결
- 제거 경로: `TryResolveStep`, 샘플 단차 상승량, 숨은 Transform fallback 없음
- 회귀: 경사면 관측값이 위쪽 또는 뒤쪽 이동량을 직접 생성하지 않음
